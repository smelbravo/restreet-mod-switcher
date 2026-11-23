# ReStreet Mod Switcher

[English](#english) · [Português de Portugal](#português-de-portugal)

## English

ReStreet Mod Switcher is a Windows launcher fork of [SamuelitoDaVila’s ReStreet](https://github.com/samuelit0davila/fifa-street-pc-port), the PC port of FIFA Street (2012). It adds a **Mods** tab for switching between **Vanilla**, **FIFA Street Classic** and **FIFA Street 2026**. Each mod runs offline with its own game folder and World Tour saves. Click a card to select its profile and double-click it to open its gallery.

### Install from GitHub Releases

1. Download **ReStreet-Mod-Switcher-Setup.exe** from the [Windows releases](https://github.com/smelbravo/restreet-mod-switcher/releases).
2. Run the v0.2.0 installer, choose an empty destination and select your own FIFA Street (2012) Xbox 360 ISO. An internet connection is required. Setup downloads [ReStreet 2.1.5](https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.1.5-online-beta), checks its SHA-256, installs the base game from your ISO and adds the Mod Switcher launcher.
3. Open `FifaStreetLauncher.exe` in that folder and test **Vanilla**. Windows x64 and the WebView2 Runtime are required for the Mods interface. Back up `Documents\fifastreet` before installing mods.

To build the installer from source, run `./Installer/BuildSwitcherSetup.ps1 -Output ./dist/ReStreet-Mod-Switcher-Setup.exe` with the .NET 8 SDK on Windows.

### Update an existing installation

To update from an earlier build, including the previous v0.2.0, close the game and launcher, then select your existing Mod Switcher folder in the v0.2.0 installer. The update keeps your game and mod files, settings and World Tour save locations. With this build, the launcher can offer future Mod Switcher releases with a verified installer.

### Find mods on Thunderstore

Open **Mods → Thunderstore** to browse the ReStreet community. **View package** opens the author’s page and installation instructions. **Download ZIP** starts the download in your browser. Follow the author’s instructions to install the package.

In v0.2.0, use **Select Game folder for this package** after installing the package in a separate `Game` folder. Its profile is available under **Thunderstore profiles** and has its own saves. Open the team catalogue from **Mods** or **Matches** and click a team crest to see the squad, positions, and player attributes. The World Tour squad is read from the active profile's save when it can be identified.

### Install the manual profiles

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

### Private matches in Vanilla

In v0.2.0, select **Vanilla** and open **Online** to set your nickname and region. Turn on **Play online**. Both players should select the same region. One creates a room under **Matches**, chooses **5-a-side**, **6-a-side** or **Futsal**, and can set a password. Each player controls a team. Your friends and rankings are linked to your nickname and region. [ReStreet player guide](https://samuelit0davila.github.io/fifa-street-pc-port/assets/guides/ReStreet-Guide-EN.pdf)

## Português de Portugal

O ReStreet Mod Switcher é um fork para Windows do [ReStreet de SamuelitoDaVila](https://github.com/samuelit0davila/fifa-street-pc-port), o port de FIFA Street (2012) para PC. Acrescenta a aba **Mods** para alternar entre **Vanilla**, **FIFA Street Classic** e **FIFA Street 2026**. Cada mod arranca offline com a sua própria pasta de jogo e dados do Tour Mundial. Um clique seleciona o perfil e um duplo clique abre a galeria.

### Instalar a partir das Releases do GitHub

1. Descarrega **ReStreet-Mod-Switcher-Setup.exe** das [releases para Windows](https://github.com/smelbravo/restreet-mod-switcher/releases).
2. Abre o instalador v0.2.0, escolhe uma pasta de destino vazia e seleciona o teu ISO de FIFA Street (2012) para Xbox 360. É necessária ligação à Internet. O instalador descarrega o [ReStreet 2.1.5](https://github.com/samuelit0davila/fifa-street-pc-port/releases/tag/v2.1.5-online-beta), verifica o SHA-256, instala o jogo a partir do teu ISO e acrescenta o launcher do Mod Switcher.
3. Abre `FifaStreetLauncher.exe` nessa pasta e testa o **Vanilla**. A interface Mods requer Windows x64 e WebView2 Runtime. Faz uma cópia de segurança de `Documentos\fifastreet` antes de instalares os mods.

Para compilar o instalador a partir do código, executa `./Installer/BuildSwitcherSetup.ps1 -Output ./dist/ReStreet-Mod-Switcher-Setup.exe` no Windows com o SDK do .NET 8.

### Atualizar uma instalação existente

Para atualizares uma build anterior, incluindo a v0.2.0 antiga, fecha o jogo e o launcher e seleciona a pasta atual do Mod Switcher no instalador v0.2.0. A atualização mantém os ficheiros do jogo e dos mods, as definições e as pastas de gravação do Tour Mundial. Com esta build, o launcher pode avisar quando sair uma nova versão do Mod Switcher com um instalador verificável.

### Encontrar mods no Thunderstore

Abre **Mods → Thunderstore** para consultar a comunidade ReStreet. **Ver pacote** abre a página e as instruções do autor. **Descarregar ZIP** inicia a transferência no navegador. Segue as instruções do autor para instalar o pacote.

Na v0.2.0, usa **Selecionar pasta Game deste pacote** depois de o instalares numa pasta `Game` separada. O perfil fica disponível em **Perfis do Thunderstore** e tem os seus próprios saves. Abre o catálogo de equipas em **Mods** ou **Partidas** e clica num emblema para veres o plantel, as posições e os atributos dos jogadores. O plantel do Tour Mundial é lido do save do perfil ativo quando é possível identificá-lo.

### Instalar os perfis manuais

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

### Partidas privadas em Vanilla

Na v0.2.0, seleciona **Vanilla** e abre **Online** para definires o teu nick e a região. Liga **Jogar online**. Os dois jogadores devem escolher a mesma região. Um cria a sala em **Partidas**, escolhe **5 contra 5**, **6 contra 6** ou **Futsal** e pode definir uma palavra-passe. Cada jogador controla uma equipa. Os amigos e as classificações estão associados ao nick e à região. [Guia do jogador do ReStreet](https://samuelit0davila.github.io/fifa-street-pc-port/assets/guides/ReStreet-Guide-PT.pdf)

## Credits and licence / Créditos e licença

- ReStreet port and original launcher / Port ReStreet e launcher original: **SamuelitoDaVila**. [Original repository / Repositório original](https://github.com/samuelit0davila/fifa-street-pc-port) · [Official website / Site oficial](https://samuelit0davila.github.io/fifa-street-pc-port/) · [Official Discord / Discord oficial](https://discord.gg/CqjYMGKyy2)
- **FIFA Street Classic** by Giovani Biava and the Classic Patch contributors. [Mod page on Patreon](https://www.patreon.com/biavva/posts/patch-fifa-beta-173393762) · [Download](https://t.co/m5EXewDc84) · [Giovani Biava on X](https://x.com/giovanibiava)
- **FIFA Street 2026** by Jespindola. [Mod download on Google Drive](https://drive.google.com/file/d/1XKbAZtVdSmj5AMh3z9BkJjzxTupsrrLO/view) · [Jespindola on X](https://x.com/Jespindola15_)
- See / Consulta [CREDITS.md](CREDITS.md) and / e [LICENSE](LICENSE).
