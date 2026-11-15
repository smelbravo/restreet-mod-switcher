# ReStreet Mod Switcher

[English](#english) · [Português de Portugal](#português-de-portugal)

## English

ReStreet Mod Switcher is a Windows launcher fork of [SamuelitoDaVila’s ReStreet](https://github.com/samuelit0davila/fifa-street-pc-port), the PC port of FIFA Street (2012). It adds a **Mods** tab for switching between **Vanilla**, **FIFA Street Classic** and **FIFA Street 2026**. Each mod runs offline with its own game folder and World Tour saves. Click a card to select its profile and double-click it to open its gallery.

The repository contains the launcher source. You need your own game, a working ReStreet installation and separate downloads for the mods. The download buttons open the authors’ download pages in your browser.

### Install the launcher

1. Install and test [ReStreet 2.1.4](https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.1.4-online-beta) with your own copy of FIFA Street. This is the version used to test the mod profiles.
2. Copy the working ReStreet installation to a new folder, such as `D:\Games\ReStreet Mod Switcher`. Keep its `Game` and `Binaries` folders. Back up your Windows Documents folder’s `fifastreet` directory before running any patch installer.
3. Install the .NET 8 SDK and WebView2 Runtime. Clone this repository, open PowerShell in the clone and build the launcher:

   ```powershell
   dotnet publish Launcher/FifaStreetLauncher/FifaStreetLauncher.csproj -c Release -o launcher-build
   ```

4. Close ReStreet. Copy the **contents** of `launcher-build` into the new installation folder, alongside `Game` and `Binaries`. Run `FifaStreetLauncher.exe` there and test **Vanilla** first.

### Install the mods manually

1. In the new installation, make two independent copies of the clean `Game` folder at `Mods\Classic\Game` and `Mods\2026\Game`. Each copy should directly contain `default.xex` and `data0.big`.
2. **Classic.** [Download the ZIP](https://sharemods.com/8i7py52glnt4/FIFAStreetClassic.zip.html), extract it and run the patch installer. Use **Browse** and explicitly select `Mods\Classic\Game` before installing. [Author’s post](https://www.patreon.com/biavva/posts/patch-fifa-beta-173393762)
3. **2026.** [Download the ZIP](https://drive.google.com/file/d/1XKbAZtVdSmj5AMh3z9BkJjzxTupsrrLO/view). Extract the folder containing the installer beside `Mods\2026\Game`, inside `Mods\2026`. The installer may move old saves. For a package whose `instalar.ps1` supports `FS_DOCS`, open PowerShell in the extracted installer folder and run:

   ```powershell
   $env:FS_DOCS = Join-Path (Get-Location).Path 'InstallerDocuments'
   try { & .\instalar.ps1 } finally { Remove-Item Env:FS_DOCS }
   ```

   Confirm that the installer identifies `Mods\2026\Game` as its target. If your package has a different installer or does not support `FS_DOCS`, check its instructions and back up your saves before proceeding. [Author’s download post](https://x.com/Jespindola15_/status/2114894270738129390)
4. Open **Mods**, select each mod and press **Select mod Game folder** to set its path. To change profiles, exit the game, select a card and press **Play**. A new mod profile may ask you to create its own save. Vanilla uses `Documents\fifastreet` by default. Mod saves are under its `ModProfiles` subfolder.

The launcher includes a European Portuguese UI. The in-game PT-PT option also needs a separate `locale_ptpt.big` in the chosen profile’s `Game\data\loc`. This source checkout does not include those game archives.

## Português de Portugal

O ReStreet Mod Switcher é um fork para Windows do [ReStreet de SamuelitoDaVila](https://github.com/samuelit0davila/fifa-street-pc-port), o port de FIFA Street (2012) para PC. Acrescenta a aba **Mods** para alternar entre **Vanilla**, **FIFA Street Classic** e **FIFA Street 2026**. Cada mod arranca offline com a sua própria pasta de jogo e dados do Tour Mundial. Um clique seleciona o perfil e um duplo clique abre a galeria.

O repositório contém o código do launcher. Precisas da tua cópia do jogo, de uma instalação funcional do ReStreet e dos downloads dos mods. Os botões de descarga abrem as páginas dos autores no navegador.

### Instalar o launcher

1. Instala e testa o [ReStreet 2.1.4](https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.1.4-online-beta) com a tua cópia do FIFA Street. Foi a versão usada para testar os perfis de mods.
2. Copia a instalação funcional para uma nova pasta, por exemplo `D:\Jogos\ReStreet Mod Switcher`. Mantém as pastas `Game` e `Binaries`. Faz uma cópia de segurança da pasta `fifastreet` nos Documentos do Windows antes de correres instaladores de mods.
3. Instala o SDK do .NET 8 e o WebView2 Runtime. Clona este repositório, abre o PowerShell na pasta do clone e compila:

   ```powershell
   dotnet publish Launcher/FifaStreetLauncher/FifaStreetLauncher.csproj -c Release -o launcher-build
   ```

4. Fecha o ReStreet. Copia o **conteúdo** de `launcher-build` para a nova instalação, ao lado de `Game` e `Binaries`. Abre `FifaStreetLauncher.exe` nessa pasta e testa primeiro o **Vanilla**.

### Instalar os mods manualmente

1. Na nova instalação, faz duas cópias independentes da pasta `Game` limpa em `Mods\Classic\Game` e `Mods\2026\Game`. Cada cópia deve conter diretamente `default.xex` e `data0.big`.
2. **Classic.** [Descarrega o ZIP](https://sharemods.com/8i7py52glnt4/FIFAStreetClassic.zip.html), extrai-o e abre o instalador do patch. Em **Procurar**, indica explicitamente `Mods\Classic\Game` antes de instalar. [Publicação do autor](https://www.patreon.com/biavva/posts/patch-fifa-beta-173393762)
3. **2026.** [Descarrega o ZIP](https://drive.google.com/file/d/1XKbAZtVdSmj5AMh3z9BkJjzxTupsrrLO/view). Extrai a pasta que contém o instalador para dentro de `Mods\2026`, ao lado da pasta `Game`. O instalador pode mover dados de gravação antigos. Se o `instalar.ps1` do pacote aceitar `FS_DOCS`, abre o PowerShell na pasta extraída do instalador e executa:

   ```powershell
   $env:FS_DOCS = Join-Path (Get-Location).Path 'InstallerDocuments'
   try { & .\instalar.ps1 } finally { Remove-Item Env:FS_DOCS }
   ```

   Confirma que o instalador identifica `Mods\2026\Game` como destino. Se o pacote trouxer outro instalador ou não aceitar `FS_DOCS`, verifica as instruções e faz uma cópia de segurança dos saves antes de avançar. [Publicação de download do autor](https://x.com/Jespindola15_/status/2114894270738129390)
4. Abre **Mods**, seleciona cada mod e usa **Selecionar pasta Game do mod** para indicar o caminho. Para trocares de perfil, fecha o jogo, escolhe um cartão e carrega em **Jogar**. Um perfil novo pode pedir para criar um ficheiro de gravação próprio. O Vanilla usa `Documentos\fifastreet` por defeito. Os saves dos mods ficam na subpasta `ModProfiles`.

O launcher inclui uma interface em português de Portugal. A opção PT-PT dentro do jogo também precisa de um ficheiro `locale_ptpt.big` na pasta `Game\data\loc` do perfil escolhido. Estes ficheiros de jogo são externos ao repositório.

## Credits and licence / Créditos e licença

- ReStreet port and original launcher / Port ReStreet e launcher original: **SamuelitoDaVila**.
- FIFA Street Classic: **Giovani Biava** and the Classic Patch contributors.
- FIFA Street 2026: **Jespindola**.
- See / Consulta [CREDITS.md](CREDITS.md) and / e [LICENSE](LICENSE).
