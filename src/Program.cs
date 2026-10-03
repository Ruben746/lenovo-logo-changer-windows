using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LenovoLogoChanger {
    static class Native {
        [StructLayout(LayoutKind.Sequential)] struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct Privilege { public uint Count; public Luid Id; public uint Flags; }
        [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool LookupPrivilegeValue(string system, string name, out Luid luid);
        [DllImport("advapi32.dll", SetLastError=true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disable, ref Privilege privilege, uint length, IntPtr old, IntPtr size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetFirmwareEnvironmentVariableEx(string name, string guid, byte[] bytes, uint size, out uint attrs);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetFirmwareEnvironmentVariableEx(string name, string guid, byte[] bytes, uint size, uint attrs);
        public static bool IsAdmin() { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        public static void Privileges() {
            IntPtr token;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28, out token)) Fail("Administrator rights required");
            try {
                Luid luid;
                if (!LookupPrivilegeValue(null, "SeSystemEnvironmentPrivilege", out luid)) Fail("UEFI privilege unavailable");
                Privilege p = new Privilege { Count=1, Id=luid, Flags=2 };
                if (!AdjustTokenPrivileges(token, false, ref p, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error()!=0) Fail("Run as administrator to access UEFI variables");
            } finally { CloseHandle(token); }
        }
        static string GuidFor(string name) { return name=="LBLDESP" ? "{871455D0-5576-4FB8-9865-AF0824463B9E}" : "{871455D1-5576-4FB8-9865-AF0824463C9F}"; }
        public static byte[] Read(string name) {
            byte[] buffer=new byte[4096]; uint attrs;
            uint n=GetFirmwareEnvironmentVariableEx(name, GuidFor(name), buffer, (uint)buffer.Length, out attrs);
            if (n==0) Fail("Read " + name);
            if (n!=(name=="LBLDESP"?10:40) || attrs!=7) throw new Exception("Unexpected UEFI variable layout or attributes; nothing written.");
            return buffer.Take((int)n).ToArray();
        }
        public static void Write(string name, byte[] bytes) {
            if (!SetFirmwareEnvironmentVariableEx(name, GuidFor(name), bytes, (uint)bytes.Length, 7)) Fail("Write " + name);
            if (!Read(name).SequenceEqual(bytes)) throw new Exception("Read-back check failed: " + name);
        }
        static void Fail(string what) { throw new Exception(what + " (Windows error " + Marshal.GetLastWin32Error() + ")"); }
    }

    static class Engine {
        static readonly string[] FormatNames = { "jpg", "tga", "pcx", "gif", "bmp", "png" };
        public const uint Crc32Protocol=0x20000, Sha256Protocol=0x20003;
        public static uint Version(byte[] dvc) { return BitConverter.ToUInt32(dvc,0); }
        public static uint Width(byte[] esp) { return BitConverter.ToUInt32(esp,1); }
        public static uint Height(byte[] esp) { return BitConverter.ToUInt32(esp,5); }
        public static bool Supports(byte[] esp, string format) { return (esp[9] & (1<<Array.IndexOf(FormatNames,format)))!=0; }
        public static string Formats(byte bits) { return string.Join(" / ", FormatNames.Where((n,i) => (bits & (1<<i))!=0)); }
        public static bool IsGif(byte[] bytes) { return Format(bytes)=="gif"; }
        // File type from magic bytes, as stored on the ESP. Only formats GDI+ can verify are produced.
        public static string Format(byte[] b) {
            if(b.Length>=6 && (Encoding.ASCII.GetString(b,0,6)=="GIF87a" || Encoding.ASCII.GetString(b,0,6)=="GIF89a")) return "gif";
            if(b.Length>=2 && b[0]==66 && b[1]==77) return "bmp";
            if(b.Length>=8 && b[0]==0x89 && b[1]==0x50 && b[2]==0x4E && b[3]==0x47) return "png";
            if(b.Length>=3 && b[0]==0xFF && b[1]==0xD8 && b[2]==0xFF) return "jpg";
            return null;
        }
        static string FileName(byte[] bytes, byte[] esp) { return "mylogo_" + Width(esp) + "x" + Height(esp) + "." + Format(bytes); }
        public static void ValidatePrepared(byte[] bytes, byte[] esp) {
            string format=Format(bytes);
            if(format==null || !Supports(esp,format)) throw new Exception("Format not supported by this firmware: " + (format??"unknown") + ".");
            if(format=="bmp") ValidateBmp(bytes, esp);
            using(var stream=new MemoryStream(bytes)) using(var image=Image.FromStream(stream,false,true)) {
                if(image.Width<1 || image.Height<1 || image.Width>Width(esp) || image.Height>Height(esp)) throw new Exception("Image dimensions out of range.");
                foreach(var guid in image.FrameDimensionsList) {
                    var dimension=new FrameDimension(guid); int frames=image.GetFrameCount(dimension);
                    if(frames<1 || frames>500 || (frames>1 && format!="gif")) throw new Exception("Invalid frame count.");
                    using(var surface=new Bitmap(image.Width,image.Height,PixelFormat.Format24bppRgb)) using(var g=Graphics.FromImage(surface)) {
                        for(int i=0;i<frames;i++) { image.SelectActiveFrame(dimension,i); g.DrawImageUnscaled(image,0,0); }
                    }
                }
            }
        }
        static string Reg(string path, string name) { using(var k=Registry.LocalMachine.OpenSubKey(path)) { return k==null?"":Convert.ToString(k.GetValue(name)); } }
        public static string Identity() {
            const string key="HARDWARE\\DESCRIPTION\\System\\BIOS";
            string manufacturer=Reg(key,"SystemManufacturer");
            if (manufacturer!="LENOVO") throw new Exception("Not a Lenovo PC (found: " + manufacturer + ").");
            return manufacturer + " " + Reg(key,"SystemProductName") + " · " + Reg(key,"SystemFamily") + " · BIOS " + Reg(key,"BIOSVersion");
        }
        // Accepts the two protocols the original project implements; anything else is refused before any write.
        public static void ValidateVars(byte[] esp, byte[] dvc) {
            if (esp.Length!=10 || dvc.Length!=40) throw new Exception("Unexpected Lenovo logo variable sizes; nothing written.");
            uint v=Version(dvc);
            if (v!=Crc32Protocol && v!=Sha256Protocol) throw new Exception("Unknown Lenovo logo protocol 0x" + v.ToString("x") + "; nothing written.");
            if (Width(esp)<1 || Width(esp)>8192 || Height(esp)<1 || Height(esp)>8192 || (esp[9]&0x3F)==0)
                throw new Exception("Implausible logo size or format list reported by the firmware; nothing written.");
        }
        public static uint Crc(byte[] bytes) {
            uint crc=0xffffffff;
            for(int i=0;i<Math.Min(512,bytes.Length);i++) { crc^=bytes[i]; for(int j=0;j<8;j++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u); }
            return ~crc;
        }
        static byte[] Sha256(byte[] bytes) { using(var h=SHA256.Create()) return h.ComputeHash(bytes); }
        // 0x20000: CRC32 of the first 512 bytes in LBLDVC[4..8]. 0x20003: SHA-256 of the file in LBLDVC[4..36].
        public static byte[] Checksum(byte[] image, byte[] dvc) { return Version(dvc)==Crc32Protocol ? BitConverter.GetBytes(Crc(image)) : Sha256(image); }
        static int ChecksumArea(byte[] dvc) { return Version(dvc)==Crc32Protocol ? 4 : 36; }
        public static string ChecksumText(byte[] image, byte[] dvc) {
            return Version(dvc)==Crc32Protocol ? "CRC32(512) " + Crc(image).ToString("X8") : "SHA-256 " + BitConverter.ToString(Sha256(image)).Replace("-","");
        }
        public static byte[] Prepare(string path, byte[] esp) {
            var info=new FileInfo(path);
            if (!info.Exists || info.Length==0 || info.Length>16*1024*1024) throw new Exception("Image missing, empty or larger than 16 MiB.");
            byte[] original=File.ReadAllBytes(path);
            using(var stream=new MemoryStream(original))
            using(var image=Image.FromStream(stream,false,true)) {
                if (image.Width<1 || image.Height<1 || image.Width>Width(esp) || image.Height>Height(esp)) throw new Exception("Image must fit in " + Width(esp) + " x " + Height(esp) + " (no automatic resize).");
                string format=Format(original);
                // GIFs are kept byte for byte with all frames, as the original project does.
                if(format=="gif") { if(!Supports(esp,"gif")) throw new Exception("Firmware does not declare GIF support."); ValidatePrepared(original,esp); return original; }
                if (image.FrameDimensionsList.Any(g => image.GetFrameCount(new FrameDimension(g))>1)) throw new Exception("Multi-frame images are only supported as GIF.");
                // Prefer a plain 24-bit BMP, the simplest format for firmware decoders; otherwise keep the file as is.
                if(!Supports(esp,"bmp")) {
                    if(format==null || !Supports(esp,format)) throw new Exception("Use one of the formats this firmware supports: " + Formats(esp[9]) + ".");
                    ValidatePrepared(original,esp); return original;
                }
                using(var bmp=new Bitmap(image.Width,image.Height,PixelFormat.Format24bppRgb)) {
                    using(var g=Graphics.FromImage(bmp)) { g.Clear(Color.Black); g.DrawImage(image,new Rectangle(0,0,bmp.Width,bmp.Height)); } // Pixel size: DrawImageUnscaled would apply the file DPI.
                    using(var output=new MemoryStream()) { bmp.Save(output,ImageFormat.Bmp); byte[] data=output.ToArray(); ValidatePrepared(data,esp); return data; }
                }
            }
        }
        static void ValidateBmp(byte[] data, byte[] esp) {
            if(data.Length<54 || BitConverter.ToUInt32(data,14)!=40 || BitConverter.ToUInt16(data,28)!=24 || BitConverter.ToUInt32(data,30)!=0)
                throw new Exception("Expected BMP with BITMAPINFOHEADER, 24-bit, uncompressed.");
            int w=BitConverter.ToInt32(data,18), h=BitConverter.ToInt32(data,22);
            if(w<1 || w>Width(esp) || h<1 || h>Height(esp) || BitConverter.ToUInt32(data,10)!=54 || data.Length!=54+((w*3+3)&~3)*h || BitConverter.ToUInt32(data,2)!=data.Length)
                throw new Exception("Inconsistent BMP structure.");
        }
        public static void Read(out byte[] esp, out byte[] dvc) {
            Identity(); Native.Privileges();
            esp=Native.Read("LBLDESP"); dvc=Native.Read("LBLDVC"); ValidateVars(esp,dvc);
        }
        // Same model as the original project: the image lives in EFI/Lenovo/Logo, LBLDVC holds its checksum,
        // LBLDESP[0] enables it. Replacing a logo needs no prior restore.
        public static void Apply(byte[] image) {
            byte[] esp, dvc; Read(out esp, out dvc); ValidatePrepared(image,esp);
            byte[] newEsp=(byte[])esp.Clone(), newDvc=(byte[])dvc.Clone(), sum=Checksum(image,dvc);
            newEsp[0]=1; Array.Copy(sum,0,newDvc,4,sum.Length);
            using(var mount=new EspMount()) {
                string folder=LogoFolder(mount.Root), target=Path.Combine(folder,FileName(image,esp));
                try {
                    if(Directory.Exists(folder)) Directory.Delete(folder,true);
                    Directory.CreateDirectory(folder);
                    using(var f=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { f.Write(image,0,image.Length); f.Flush(true); }
                    if(!Sha256(File.ReadAllBytes(target)).SequenceEqual(Sha256(image))) throw new Exception("ESP copy verification failed.");
                    Native.Write("LBLDVC",newDvc); // Checksum first, then enable.
                    Native.Write("LBLDESP",newEsp);
                } catch(Exception e) {
                    try { RestoreMounted(mount.Root); }
                    catch(Exception rollback) { throw new Exception("FAILED: " + e.Message + "\nRollback incomplete: " + rollback.Message + "\nDo not reboot before checking."); }
                    throw new Exception("Failed, default logo restored: " + e.Message);
                }
            }
        }
        // Same as the original project's Restore Logo: disable, clear the checksum, remove EFI/Lenovo/Logo.
        public static void Restore() {
            byte[] esp, dvc; Read(out esp, out dvc);
            using(var mount=new EspMount()) RestoreMounted(mount.Root);
        }
        static void RestoreMounted(string root) {
            byte[] esp=Native.Read("LBLDESP"), dvc=Native.Read("LBLDVC"); ValidateVars(esp,dvc);
            // Disable before touching the checksum or files, so the firmware never points at a missing image.
            if(esp[0]!=0) { esp[0]=0; Native.Write("LBLDESP",esp); }
            int area=ChecksumArea(dvc);
            if(dvc.Skip(4).Take(area).Any(b => b!=0)) { Array.Clear(dvc,4,area); Native.Write("LBLDVC",dvc); }
            string folder=LogoFolder(root);
            if(Directory.Exists(folder)) Directory.Delete(folder,true);
        }
        static string LogoFolder(string root) { return Path.Combine(root,"EFI","Lenovo","Logo"); }
    }

    // Windows loading circle, as in the original project: bcdedit bootuxdisabled on/off on {current}.
    static class BootUx {
        // bcdedit prints Yes/No in the Windows display language; any value not in this list counts as "Yes".
        // ponytail: localized word list, extend it if a language reports the circle state wrongly.
        static readonly string[] No = { "No", "Non", "Nein", "Não", "Nee", "Nie", "Nem", "Ne", "Nej", "Ei", "Hayır", "Нет", "Ні", "Όχι", "否", "いいえ", "아니요", "Không", "Tidak" };
        public static bool ParseShown(string output) {
            foreach(string line in output.Split('\n')) {
                string[] parts=line.Trim().Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
                if(parts.Length==2 && parts[0]=="bootuxdisabled") return No.Contains(parts[1]);
            }
            return true;
        }
        static string Bcdedit(string arguments) {
            var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"bcdedit.exe"),arguments) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true,
                StandardOutputEncoding=Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage) };
            using(var p=Process.Start(start)) {
                string stdout=p.StandardOutput.ReadToEnd(), stderr=p.StandardError.ReadToEnd(); p.WaitForExit();
                if(p.ExitCode!=0) throw new Exception("bcdedit " + arguments + ": " + stdout + stderr);
                return stdout;
            }
        }
        public static bool Shown() { return ParseShown(Bcdedit("/enum {current}")); }
        public static void Show(bool show) {
            Bcdedit("/set {current} bootuxdisabled " + (show?"off":"on"));
        }
    }

    sealed class EspMount:IDisposable {
        public string Root;
        static void Run(string root,string option) {
            using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"mountvol.exe"),root+" "+option) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardError=true, RedirectStandardOutput=true })) {
                string stdout=p.StandardOutput.ReadToEnd(), stderr=p.StandardError.ReadToEnd(); p.WaitForExit();
                if(p.ExitCode!=0) throw new Exception("mountvol " + option + ": " + stdout + stderr);
            }
        }
        public EspMount() {
            var used=DriveInfo.GetDrives().Select(d=>d.Name.Substring(0,1).ToUpperInvariant()).ToArray();
            for(char c='Z';c>='D';c--) if(!used.Contains(c.ToString())) { Root=c+":\\"; break; }
            if(Root==null) throw new Exception("No free drive letter to mount the EFI partition.");
            Run(Root,"/s");
        }
        public void Dispose() { Run(Root,"/d"); }
    }

    static class I18n {
        public static readonly string[] Names = { "English", "中文", "Français" };
        // Keys and English/Chinese texts follow the original project's src/i18n.rs.
        static readonly System.Collections.Generic.Dictionary<string,string[]> Texts = new System.Collections.Generic.Dictionary<string,string[]> {
            { "language", new[] { "Language", "语言", "Langue" } },
            { "supported", new[] { "Your device is supported !", "您的设备是支持的！", "Votre appareil est pris en charge !" } },
            { "unsupported", new[] { "Your device is not supported !", "不支持您的设备！", "Votre appareil n'est pas pris en charge !" } },
            { "logo_enabled", new[] { "UEFI Logo DIY Enabled", "自定义UEFI Logo已启用", "Logo UEFI personnalisé activé" } },
            { "logo_disabled", new[] { "UEFI Logo DIY Disabled", "自定义UEFI Logo未启用", "Logo UEFI personnalisé désactivé" } },
            { "max_image_size", new[] { "Max Image Size", "图片最大分辨率", "Taille maximale de l'image" } },
            { "supported_formats", new[] { "Support Format", "支持的图片格式", "Formats pris en charge" } },
            { "version", new[] { "Version", "协议版本", "Version du protocole" } },
            { "show_windows_loading", new[] { "Show Windows loading circle", "显示Windows加载图标", "Afficher le cercle de chargement Windows" } },
            { "pick_image", new[] { "Pick Image", "选择图片", "Choisir une image" } },
            { "picked_image", new[] { "Picked Image:", "已选择的图片：", "Image sélectionnée :" } },
            { "change_logo_btn", new[] { "!!! Change Logo !!!", "!!! 设置Logo !!!", "!!! Changer le logo !!!" } },
            { "restore_logo_btn", new[] { "Restore Logo", "恢复Logo", "Restaurer le logo" } },
            { "setting_logo_wait", new[] { "Setting logo, please wait...", "正在设置Logo，请稍候...", "Changement du logo, veuillez patienter..." } },
            { "restoring_logo_wait", new[] { "Restoring logo, please wait...", "正在恢复Logo，请稍候...", "Restauration du logo, veuillez patienter..." } },
            { "change_logo_success", new[] { "Change logo succeeded, reboot to see the effect", "设置Logo成功，重新启动以查看效果", "Logo changé, redémarrez pour voir le résultat" } },
            { "change_logo_failed", new[] { "Change logo failed", "设置Logo失败", "Échec du changement de logo" } },
            { "restore_logo_success", new[] { "Restore Logo Success", "恢复Logo成功", "Logo d'origine restauré" } },
            { "restore_logo_failed", new[] { "Restore Logo Failed", "恢复Logo失败", "Échec de la restauration du logo" } },
            { "admin_required", new[] { "You need to run this program as Administrator !", "您需要以管理员权限运行此程序！", "Vous devez lancer ce programme en tant qu'administrateur !" } },
            { "image_rejected", new[] { "Image rejected: ", "图片被拒绝：", "Image refusée : " } },
            { "gif_note", new[] { "Animated GIF: some BIOS versions only display the first frame.", "GIF动画：部分BIOS只显示第一帧。", "GIF animé : certains BIOS n'affichent que la première image." } },
            { "confirm", new[] { "Confirm", "确认", "Confirmer" } },
            { "confirm_change", new[] { "Copy this image to the EFI partition and set it as the boot logo?", "将此图片复制到EFI分区并设为启动Logo？", "Copier cette image sur la partition EFI et en faire le logo de démarrage ?" } },
            { "confirm_restore", new[] { "Restore the default Lenovo boot logo?", "恢复默认的联想启动Logo？", "Restaurer le logo de démarrage Lenovo d'origine ?" } },
        };
        public static int Lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="zh" ? 1 : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="fr" ? 2 : 0;
        public static string T(string key) { return Texts[key][Lang]; }
    }

    sealed class App:Form {
        const string Version="1.4.0";
        static readonly Color Back=Color.FromArgb(27,27,27), Fore=Color.FromArgb(210,210,210), Green=Color.FromArgb(144,238,144), Red=Color.FromArgb(255,128,128), Yellow=Color.FromArgb(255,230,120);
        readonly FlowLayoutPanel root=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, AutoScroll=true, Padding=new Padding(14) };
        readonly TextBox log=new TextBox { Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Width=800, Height=80, BackColor=Color.FromArgb(18,18,18), ForeColor=Fore, BorderStyle=BorderStyle.FixedSingle };
        readonly PictureBox preview=new PictureBox { SizeMode=PictureBoxSizeMode.Zoom, Width=320, Height=180, BackColor=Color.Black };
        readonly bool admin=Native.IsAdmin();
        byte[] esp, dvc, prepared; string picked, statusKey; Color statusColor; bool showLoading=true;

        public App() {
            Text="Lenovo UEFI Boot Logo Changer"; ClientSize=new Size(850,820);
            BackColor=Back; ForeColor=Fore; Font=new Font("Segoe UI",10f);
            try { Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Controls.Add(root);
            if(admin) ReadState();
            Render();
        }
        void Log(string text) { log.AppendText(text+Environment.NewLine); }
        void ReadState() {
            // One copyable line per start: what to paste in a compatibility report.
            string device; try { device=Engine.Identity(); } catch(Exception e) { device=e.Message; }
            try {
                Engine.Read(out esp, out dvc);
                Log(device+" | protocol 0x"+Engine.Version(dvc).ToString("x")+" | "+Engine.Width(esp)+"x"+Engine.Height(esp)+" | "+Engine.Formats(esp[9])+" | enabled "+esp[0]);
            } catch(Exception e) { esp=dvc=null; Log(device+" | "+e.Message); }
            try { showLoading=BootUx.Shown(); } catch(Exception e) { Log(e.Message); }
        }

        // Rebuilt on every state change, like the original egui immediate-mode UI.
        void Render() {
            root.SuspendLayout();
            foreach(Control c in root.Controls.Cast<Control>().ToArray()) if(c!=log && c!=preview) c.Dispose();
            root.Controls.Clear();
            var langs=new FlowLayoutPanel { AutoSize=true, WrapContents=false };
            langs.Controls.Add(new Label { Text=I18n.T("language")+" : ", AutoSize=true, Margin=new Padding(0,5,0,0) });
            for(int i=0;i<I18n.Names.Length;i++) {
                int lang=i; var r=new RadioButton { Text=I18n.Names[i], AutoSize=true, Checked=I18n.Lang==i };
                r.CheckedChanged+=(s,e)=> { if(r.Checked && I18n.Lang!=lang) { I18n.Lang=lang; BeginInvoke(new Action(Render)); } };
                langs.Controls.Add(r);
            }
            root.Controls.Add(langs); Separator();
            if(!admin) { Label(I18n.T("admin_required"),Red); }
            else if(esp==null) { Label(I18n.T("unsupported"),Red); }
            else {
                string identity; try { identity=Engine.Identity(); } catch(Exception e) { identity=e.Message; }
                Label(I18n.T("supported"),Green); Label(identity,Fore); Separator();
                Label(esp[0]!=0?I18n.T("logo_enabled"):I18n.T("logo_disabled"), esp[0]!=0?Green:Red);
                Label(I18n.T("max_image_size")+" : "+Engine.Width(esp)+"x"+Engine.Height(esp),Fore);
                Label(I18n.T("supported_formats")+" : "+Engine.Formats(esp[9]),Fore);
                Label(I18n.T("version")+" : "+Engine.Version(dvc).ToString("x"),Fore);
                Separator();
                var loading=new CheckBox { Text=I18n.T("show_windows_loading"), Checked=showLoading, AutoSize=true };
                loading.CheckedChanged+=(s,e)=>showLoading=loading.Checked; root.Controls.Add(loading);
                Button(I18n.T("pick_image"),Fore,Pick);
                if(picked!=null) {
                    Label(I18n.T("picked_image")+" "+picked,Fore);
                    if(prepared!=null) {
                        root.Controls.Add(preview);
                        if(Engine.IsGif(prepared)) Label(I18n.T("gif_note"),Yellow);
                        Button(I18n.T("change_logo_btn"),Color.Red,Apply);
                    }
                }
                if(statusKey!=null) Label(I18n.T(statusKey),statusColor);
                Separator();
                Button(I18n.T("restore_logo_btn"),Fore,Restore);
            }
            Separator(); root.Controls.Add(log);
            var footer=new LinkLabel { Text="Free & Open · v"+Version+" · MIT · based on chnzzh/lenovo-logo-changer", AutoSize=true, UseMnemonic=false, LinkColor=Color.FromArgb(120,170,255), ActiveLinkColor=Color.White };
            footer.LinkArea=new LinkArea(footer.Text.IndexOf("chnzzh"),"chnzzh/lenovo-logo-changer".Length);
            footer.LinkClicked+=(s,e)=>Process.Start("https://github.com/chnzzh/lenovo-logo-changer");
            root.Controls.Add(footer);
            root.ResumeLayout();
        }
        void Label(string text, Color color) { root.Controls.Add(new Label { Text=text, ForeColor=color, AutoSize=true, MaximumSize=new Size(800,0), Margin=new Padding(3,3,3,3) }); }
        void Separator() { root.Controls.Add(new Label { AutoSize=false, Width=800, Height=1, BackColor=Color.FromArgb(70,70,70), Margin=new Padding(0,6,0,6) }); }
        void Button(string text, Color color, Action click) {
            var b=new Button { Text=text, ForeColor=color, BackColor=Color.FromArgb(60,60,60), FlatStyle=FlatStyle.Flat, AutoSize=true, Padding=new Padding(6,2,6,2) };
            b.FlatAppearance.BorderColor=Color.FromArgb(90,90,90); b.Click+=(s,e)=>click(); root.Controls.Add(b);
        }
        void Pick() {
            using(var dialog=new OpenFileDialog { Filter="Image|*.png;*.jpg;*.jpeg;*.bmp;*.gif|*.*|*.*" }) {
                if(dialog.ShowDialog(this)==DialogResult.OK) LoadImage(dialog.FileName);
            }
        }
        void LoadImage(string path) {
            picked=path; prepared=null; statusKey=null;
            if(preview.Image!=null) { preview.Image.Dispose(); preview.Image=null; }
            try {
                prepared=Engine.Prepare(picked,esp);
                // GDI+ needs the stream alive for the PictureBox to animate GIF frames.
                preview.Image=Image.FromStream(new MemoryStream(prepared));
                Log(picked+" : "+preview.Image.Width+"x"+preview.Image.Height+", "+Engine.Format(prepared).ToUpperInvariant()+", "+prepared.Length+" bytes, "+Engine.ChecksumText(prepared,dvc));
            } catch(Exception e) { Log(I18n.T("image_rejected")+e.Message); }
            Render();
        }
        void Apply() { Run("confirm_change","setting_logo_wait","change_logo_success","change_logo_failed",()=> { Engine.Apply(prepared); SetLoading(showLoading); }); }
        void SetLoading(bool show) { try { BootUx.Show(show); } catch(Exception e) { Log(e.Message); } }
        void Restore() { Run("confirm_restore","restoring_logo_wait","restore_logo_success","restore_logo_failed",()=> { Engine.Restore(); SetLoading(true); }); }
        void Run(string confirm, string wait, string ok, string failed, Action operation) {
            if(MessageBox.Show(this,I18n.T(confirm),I18n.T("confirm"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes) return;
            statusKey=wait; statusColor=Yellow; Render(); root.Enabled=false; UseWaitCursor=true; Update();
            try { using(new OperationLock()) operation(); statusKey=ok; statusColor=Green; }
            catch(Exception e) { statusKey=failed; statusColor=Red; Log(e.Message); }
            finally { root.Enabled=true; UseWaitCursor=false; }
            ReadState(); Render();
        }
        protected override void Dispose(bool disposing) { if(disposing && preview.Image!=null) preview.Image.Dispose(); base.Dispose(disposing); }
    }

    sealed class OperationLock:IDisposable {
        readonly System.Threading.Mutex mutex=new System.Threading.Mutex(false,"Global\\LenovoLogoChangerTransaction");
        public OperationLock() {
            bool acquired;
            try { acquired=mutex.WaitOne(0); } catch(System.Threading.AbandonedMutexException) { acquired=true; }
            if(!acquired) { mutex.Dispose(); throw new Exception("Another instance is already running an operation."); }
        }
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }

    static class Program {
        static void Error(Exception error) {
            string logfile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lenovo-logo-changer-error.txt");
            try { File.AppendAllText(logfile,DateTime.Now.ToString("s")+Environment.NewLine+error+Environment.NewLine); } catch { logfile="(log not writable)"; }
            MessageBox.Show(error.Message+"\n"+logfile,"Lenovo UEFI Boot Logo Changer",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        static byte[] Vars(uint version, uint width, uint height, byte formats, out byte[] dvc) {
            byte[] esp=new byte[10]; dvc=new byte[40];
            Array.Copy(BitConverter.GetBytes(width),0,esp,1,4); Array.Copy(BitConverter.GetBytes(height),0,esp,5,4); esp[9]=formats; Array.Copy(BitConverter.GetBytes(version),dvc,4);
            return esp;
        }
        [STAThread] static int Main(string[] args) {
            try { return Run(args); } catch(Exception error) {
                if(args.Length>0 && args[0].Contains("test")) { Console.Error.WriteLine(error); try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-error.txt"),error.ToString()); } catch { } }
                else Error(error);
                return 1;
            }
        }
        static void Check(bool condition, string what) { if(!condition) throw new Exception("Check failed: " + what); }
        static int Run(string[] args) {
            if(args.Length>0 && args[0]=="--self-test") {
                Check(Engine.Crc(Encoding.ASCII.GetBytes("123456789"))==0xcbf43926,"crc vector");
                Check(Engine.Crc(new byte[0])==0,"crc empty");
                byte[] first=Enumerable.Range(0,512).Select(i=>(byte)i).ToArray();
                Check(Engine.Crc(first)==Engine.Crc(first.Concat(new byte[]{1,2,3}).ToArray()),"crc 512 limit");
                Check(Engine.Formats(0x3F)=="jpg / tga / pcx / gif / bmp / png","formats");
                string dpiPng=Path.Combine(Path.GetTempPath(),"lenovo-logo-dpi-test.png");
                using(var white=new Bitmap(10,10)) { using(var g=Graphics.FromImage(white)) g.Clear(Color.White); white.SetResolution(300,300); white.Save(dpiPng,ImageFormat.Png); }
                byte[] dvc, esp=Vars(Engine.Crc32Protocol,1920,1080,0x3F,out dvc);
                byte[] bmp=Engine.Prepare(dpiPng,esp); File.Delete(dpiPng);
                Check(bmp[54]==255 && bmp[55]==255 && bmp[56]==255,"300 DPI image converted at pixel size"); // First BMP row is the bottom row.
                if(args.Length>1) Engine.ValidatePrepared(Engine.Prepare(args[1],esp),esp);
                Engine.ValidateVars(esp,dvc);
                byte[] dvc3; Engine.ValidateVars(Vars(Engine.Sha256Protocol,2560,1600,0x21,out dvc3),dvc3);
                Check(!BootUx.ParseShown("identifier {current}\r\nbootuxdisabled          Yes\r\n"),"bcdedit Yes");
                Check(BootUx.ParseShown("bootuxdisabled          Non\r\n") && BootUx.ParseShown("identifier {current}\r\n"),"bcdedit Non / missing");
                Check(!BootUx.ParseShown("bootuxdisabled          Oui\r\n"),"bcdedit Oui");
                Check(Engine.Format(new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A})=="png","png magic");
                Check(Engine.ChecksumText(Encoding.ASCII.GetBytes("abc"),dvc3)=="SHA-256 BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD","sha256 vector");
                dvc[0]=1; bool refused=false; try { Engine.ValidateVars(esp,dvc); } catch { refused=true; } Check(refused,"unknown protocol refusal");
                return 0;
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=(s,e)=>Error(e.Exception);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new App()); return 0;
        }
    }
}
