# Lenovo UEFI Boot Logo Changer

Change the UEFI boot logo of Lenovo laptops (IdeaPad, IdeaPad Gaming, and other models using Lenovo's `LBLDESP` / `LBLDVC` logo variables) from Windows.
A small native Windows port of [chnzzh/lenovo-logo-changer](https://github.com/chnzzh/lenovo-logo-changer), with the same simple interface and languages (English, 中文) plus **Français**.

[Français plus bas](#français)

## Usage

1. Download `Lenovo-Logo-Changer.exe` from [Releases](../../releases) (or build it, see below).
2. Right-click → **Run as administrator**. Windows SmartScreen may warn because the file is not signed: *More info* → *Run anyway*.
3. **Pick Image**: PNG, JPEG, BMP or GIF, no larger than the *Max Image Size* shown (no automatic resize).
4. **!!! Change Logo !!!**, confirm, reboot.
5. **Restore Logo** brings back the default Lenovo logo.

No BIOS flashing. Secure Boot and BitLocker are not touched.

**Show Windows loading circle** (checked by default): unchecking it hides the spinning circle under the logo, like the original project (`bcdedit /set {current} bootuxdisabled on`). It is applied together with **Change Logo**; **Restore Logo** shows the circle again. If BitLocker is enabled, keep your recovery key at hand when changing boot settings.

## Compatibility

The program works on any Lenovo PC whose firmware exposes the logo variables with one of the two protocols of the original project. It reads the maximum size and the supported formats from the firmware itself and refuses to write anything otherwise.

| Model | Machine type | BIOS | Protocol | Result |
|---|---|---|---|---|
| IdeaPad Gaming 3 15IMH05 | 81Y4 | EGCN36WW | 0x20000 | ✅ static image · animated GIF shows first frame only |

The original project's [compatibility list](https://github.com/chnzzh/lenovo-logo-changer) covers many more models using the same mechanism.

**Help add your model:** run the program as administrator. The first line of the log looks like
`LENOVO <machine type> · <family> · BIOS <version> | protocol 0x20000 | 1920x1080 | jpg / tga / pcx / gif / bmp / png | enabled 0`.
Open a [compatibility report](../../issues/new?template=compatibility.md) with that line and what you saw at boot.

## How it works

Same mechanism as the original project:

- the image is copied to the EFI partition as `EFI/Lenovo/Logo/mylogo_<width>x<height>.<ext>`;
- its checksum is written to `LBLDVC`:
  - protocol `0x20000`: CRC32 of the first 512 bytes in `LBLDVC[4..8]`,
  - protocol `0x20003`: SHA-256 of the file in `LBLDVC[4..36]`;
- `LBLDESP[0] = 1` enables the custom logo.

**Restore Logo** does exactly what the original does: disable, clear the checksum, remove `EFI/Lenovo/Logo`. There are no backup files: this is the factory state. Picking a new image while a custom logo is active simply replaces it.

Image handling: GIFs are copied byte for byte. Other images are converted to an uncompressed 24-bit BMP when the firmware supports BMP (transparency becomes black); otherwise PNG/JPEG files are copied as is if the firmware lists their format.

Safety checks before any write: Lenovo PC, variables with the expected size and attributes (`0x7`), known protocol (`0x20000` or `0x20003`), plausible size and format list, image within limits. Every firmware write is read back. If a step fails, the default logo is restored automatically.

## Animated GIF

Depends on the BIOS: some models play animated GIFs, others only show the **first frame** (see the compatibility table); if that frame is black, the screen stays black. `tools/gif-full-frames.py` rebuilds a GIF as full-size frames at your firmware's *Max Image Size* with a single palette, the simplest form for a firmware decoder, and `tools/gif-info.py` prints the frame layout of a GIF. When in doubt, use a static image.

## Build

Requires only Windows with .NET Framework 4.8 (built in):

```powershell
.\build.ps1
.\Lenovo-Logo-Changer.exe --self-test   # CRC32, SHA-256, formats, DPI, bcdedit parsing and protocol checks
```

GitHub Actions builds and tests on every push and publishes a release for `v*` tags.

## Disclaimer

Writing UEFI variables and using the BIOS image decoder are real operations on your machine. Only the models in the table above have been tested on real hardware. Use at your own risk. Not affiliated with Lenovo.

License: MIT. Credits: [@chnzzh](https://github.com/chnzzh) for the original project and the reverse-engineered protocol.

---

## Français

Change le logo de démarrage UEFI des PC portables Lenovo (IdeaPad, IdeaPad Gaming et autres modèles utilisant les variables `LBLDESP` / `LBLDVC`) depuis Windows. Portage Windows natif de [chnzzh/lenovo-logo-changer](https://github.com/chnzzh/lenovo-logo-changer), avec la même interface simple et les mêmes langues (anglais, chinois), plus le français.

### Utilisation

1. Télécharger `Lenovo-Logo-Changer.exe` dans [Releases](../../releases), ou le compiler.
2. Clic droit → **Exécuter en tant qu'administrateur**. Si SmartScreen avertit (fichier non signé) : *Informations complémentaires* → *Exécuter quand même*.
3. **Choisir une image** : PNG, JPEG, BMP ou GIF, pas plus grande que la *taille maximale* affichée.
4. **!!! Changer le logo !!!**, confirmer, redémarrer.
5. **Restaurer le logo** remet le logo Lenovo d'origine.

Aucun flash du BIOS. Secure Boot et BitLocker ne sont pas modifiés.

**Afficher le cercle de chargement Windows** (coché par défaut) : décocher masque le cercle qui tourne sous le logo, comme dans le projet d'origine (`bcdedit /set {current} bootuxdisabled on`). Le réglage est appliqué avec **Changer le logo** ; **Restaurer le logo** réaffiche le cercle. Si BitLocker est activé, gardez votre clé de récupération à portée de main lors d'un changement des paramètres de démarrage.

### Compatibilité

Le programme fonctionne sur tout PC Lenovo dont le firmware expose les variables du logo avec l'un des deux protocoles du projet d'origine (`0x20000` : CRC32, `0x20003` : SHA-256). Il lit la taille maximale et les formats dans le firmware, et refuse d'écrire si quelque chose ne correspond pas. Seuls les modèles du tableau ci-dessus ont été testés sur du vrai matériel.

**Ajouter votre modèle :** lancez le programme en administrateur, copiez la première ligne du journal et ouvrez un [rapport de compatibilité](../../issues/new?template=compatibility.md) en indiquant ce qui s'affiche au démarrage.

### Fonctionnement

L'image est copiée dans `EFI/Lenovo/Logo/`, sa somme de contrôle est écrite dans `LBLDVC`, puis `LBLDESP[0] = 1` active le logo. **Restaurer le logo** fait comme l'original : désactivation, somme de contrôle remise à zéro, suppression de `EFI/Lenovo/Logo`. Il n'y a pas de fichiers de sauvegarde, puisque c'est l'état d'usine.

### GIF animés

Cela dépend du BIOS : certains modèles animent les GIF, d'autres n'affichent que la première image (voir le tableau de compatibilité). En cas de doute, utiliser une image fixe.

### Avertissement

L'écriture de variables UEFI et l'utilisation du décodeur d'image du BIOS sont des opérations réelles. Utilisation à vos risques. Projet non affilié à Lenovo.
