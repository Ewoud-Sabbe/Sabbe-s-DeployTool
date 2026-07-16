# DeployTool — PC Voorinstallatie

Windows-app (WPF/.NET) om nieuwe of te herinstalleren pc's in één keer voor te installeren: software installeren, snelkoppelingen plaatsen en Windows-instellingen toepassen. Draait rechtstreeks vanaf een fileserver-share, zonder lokale installatie.

## Hoe het werkt

1. De app verbindt bij opstarten met de fileserver-share en scant `Installers\` en `Shortcuts\`.
2. Je krijgt één scherm met drie secties: **Software**, **Snelkoppelingen**, **Instellingen** — elk aanvinkbaar, met standaardselectie.
3. Klik **Start installatie**: geselecteerde items worden sequentieel verwerkt, met live status en een gedetailleerd logpaneel.
4. Mislukte items krijgen automatisch één nieuwe poging; daarna kan je ze individueel opnieuw proberen via de knop op die rij.
5. Alles wordt weggeschreven naar een logbestand op de share (`Logs\{computernaam}_{tijdstip}.log`), incrementeel — ook bij een crash blijft er een spoor.

## Mapstructuur op de fileserver

```
<share-root>\
 ├─ App\                    → de gepubliceerde app zelf (vanaf hier gestart)
 ├─ Installers\             → installer-bestanden (.exe / .msi), gewoon erin zetten
 ├─ Shortcuts\               → .url / .lnk / .exe bestanden, worden automatisch gedetecteerd
 ├─ Config\
 │   ├─ installers.json      → metadata per installer (naam, silent-switch, categorie, standaard)
 │   ├─ item-defaults.json   → "standaard aangevinkt" voor snelkoppelingen/instellingen
 │   └─ McCleanup\           → (optioneel) McAfee's opruimtool, nodig voor "McAfee verwijderen", zie verderop
 └─ Logs\
     └─ {computernaam}_{datum_tijd}.log
```

Standaard share-root: `\\jdstore\Installatie\# VOORINSTALLATIE\deploy map` (zie `DeployTool/App.xaml.cs`, `DefaultShareRoot`). Override lokaal via de omgevingsvariabele `DEPLOYTOOL_SHARE_ROOT` of een eerste command-line argument.

## Software toevoegen

Zet een `.exe` of `.msi` in `Installers\` op de share. De app detecteert het bestand automatisch en toont het als **"nog te configureren"** — het blokkeert de rest niet, maar wordt niet aangevinkt tot je het instelt.

Klik op **Configureren...** (of **Bewerken...** om een bestaande installer aan te passen) om in te stellen:
- Weergavenaam
- Silent-install switch (bv. `/S`, `/silent`, `/qn`) — voor `.msi` volstaat meestal `/qn`
- Categorie (optioneel)
- Standaard geselecteerd

Dit wordt centraal opgeslagen in `Config\installers.json` en geldt voor elke pc die de app draait.

`.msi`-installers worden via `msiexec.exe /qn` gestart (niet rechtstreeks uitgevoerd — dat werkt niet voor MSI's). Als een installer al op de pc geïnstalleerd staat (via de Windows Installer ProductCode, of via een naammatch in "Programma's en onderdelen" voor `.exe`-installers), wordt de installatie overgeslagen met status **"Al geïnstalleerd"** in plaats van een foutmelding.

## Snelkoppelingen toevoegen

Zet een `.url`-, `.lnk`- of `.exe`-bestand rechtstreeks in `Shortcuts\` op de share — zelfde principe als installers, geen configuratie nodig. Bij uitvoeren wordt het bestand gewoon gekopieerd naar het bureaublad van de huidige gebruiker (geen installatie, geen silent-switches — handig voor bv. een TeamViewer QuickSupport-exe voor hulp op afstand).

## Instellingen toevoegen

Instellingen zijn hardcoded C#-acties in [`DeployTool.Core/Services/SettingsCatalogService.cs`](DeployTool.Core/Services/SettingsCatalogService.cs) — geen JSON-bestand. Voeg een nieuwe `SettingAction` toe aan de lijst (naam + een `Execute`-actie) om een instelling toe te voegen; vereist een codewijziging en herpublicatie.

## Bloatware verwijderen (McAfee, NordVPN)

Twee losse instellingen — **"McAfee verwijderen"** en **"NordVPN verwijderen"** — zoeken bij het opstarten van een sessie in het register naar elke geïnstalleerde toepassing waarvan de naam "mcafee" resp. "nordvpn" bevat en verwijderen ze stil. McAfee registreert op OEM-pc's vaak meerdere losse programma's tegelijk (LiveSafe, WebAdvisor, Safe Connect, ...) — die worden allemaal meegenomen, niet enkel de eerste.

NordVPN gebruikt zijn eigen geregistreerde uninstaller. Die is Inno Setup-gebaseerd (`unins###.exe`) en heeft `/VERYSILENT` nodig — zonder die vlag toont hij een bevestigingsvenster dat zonder zichtbaar venster gewoon niets doet (lijkt op succes: snelle exitcode 0, maar het programma blijft staan). De app herkent dat patroon automatisch en voegt de juiste vlaggen toe.

**McAfee's eigen per-product-uninstallers negeren silent-vlaggen zo goed als altijd** en openen een interactief venster. McAfee's eigen opruimmotor (`mccleanup.exe`, normaal verstopt in hun MCPR-verwijdertool) zou dat moeten omzeilen, maar recente MCPR-builds weigeren bewust om `mccleanup.exe` los aan te roepen (exitcode 2 — geprobeerd met zowel de actuele als een oudere `OldCert`-ondertekende versie uit hetzelfde pakket, beide falen op dezelfde manier). Hun grafische wrapper (`McClnUI.exe`) doet wél echt werk (exitcode 0, effectieve verwijdering), maar toont ondanks `-s` alsnog een venster ("McAfee Software Removal") waar je zelf moet klikken — dus niet geschikt voor onbemande installaties.

**Huidige aanpak (best effort + fallback):** de "McAfee verwijderen"-instelling probeert eerst `mccleanup.exe` (indien aanwezig op de share — kan gedeeltelijk werken, verschilt per pc), en probeert daarna voor elk nog aanwezig McAfee-programma alsnog diens eigen geregistreerde uninstaller (dezelfde generieke aanpak als "NordVPN verwijderen", inclusief de `/I`-naar-`/X`- en Inno Setup-fixes). Dit lost niet gegarandeerd alles op — als er na een sessie nog McAfee-vermeldingen overblijven, geeft de foutmelding exact aan welke.

**(Optioneel) de opruimtool verkrijgen en op de share zetten:**
1. Download `MCPR.exe` (McAfee Consumer Product Removal tool) van `https://download.mcafee.com/molbin/iss-loc/SupportTools/MCPR/MCPR.exe`.
2. Pak het uit met 7-Zip (`7z x MCPR.exe`) — dit hoeft niet uitgevoerd te worden, gewoon uitpakken volstaat en vermijdt de UAC-prompt/wizard volledig.
3. Kopieer de **volledige map** `$1\` naar `Config\McCleanup\` op de fileserver-share.

Als `Config\McCleanup\mccleanup.exe` ontbreekt, slaat de instelling die stap gewoon over en probeert meteen de generieke fallback per programma.

## Standaardselectie

- **Software**: via de "Standaard geselecteerd"-checkbox in de Configureren/Bewerken-dialoog.
- **Snelkoppelingen en instellingen**: via de **★ Standaard**-knop op de rij zelf. Wordt opgeslagen in `Config\item-defaults.json`.

Wijzigingen in standaardselectie gelden pas bij een volgende (verse) keer opstarten van de app — de huidige sessie's aan-/uitvinkstatus verandert niet met terugwerkende kracht.

## Projectstructuur

- **`DeployTool`** — de WPF-UI (MVVM via CommunityToolkit.Mvvm). `MainWindow` toont alles op één scherm; `Views/InstallerMetadataDialog` is het admin-schermpje.
- **`DeployTool.Core`** — alle logica los van UI: bestandsdetectie, JSON-opslag, de install-engine, logging. Herbruikt door zowel de app als de testharnas.
- **`DeployTool.TestHarness`** — console-app om de Core-laag te testen tegen een echte (test-)share, zonder de UI of adminrechten nodig te hebben.

## Bouwen en lokaal testen

```powershell
dotnet build DeployTool.slnx

# Core-logica testen zonder UI, tegen een lokale testmap:
dotnet run --project DeployTool.TestHarness\DeployTool.TestHarness.csproj -- "C:\pad\naar\testshare"

# App draaien met een andere share dan de standaard:
$env:DEPLOYTOOL_SHARE_ROOT = "C:\pad\naar\testshare"
dotnet run --project DeployTool\DeployTool.csproj
```

De app vereist adminrechten (zie `app.manifest`, `requireAdministrator`) — bij lokaal draaien via `dotnet run` vraagt Windows dus telkens om een UAC-bevestiging.

## Publiceren (deploy naar de fileserver)

```powershell
dotnet publish DeployTool\DeployTool.csproj -p:PublishProfile=FolderProfile -o "\\jdstore\Installatie\# VOORINSTALLATIE\deploy map\App"
```

Self-contained + ReadyToRun (win-x64) — een pc heeft dus geen .NET-runtime nodig, en de app start snel op ook al draait ze rechtstreeks vanaf het netwerk. Zie `DeployTool/Properties/PublishProfiles/FolderProfile.pubxml`.

## Logging

- **Live in de app**: een donker logpaneel onderaan het scherm, elke regel verschijnt zodra ze geschreven wordt.
- **Bestand**: `Logs\{computernaam}_{tijdstip}.log` op de share — timestamps op de milliseconde, kopieertijd/-grootte, volledige commandline van elke installer, exitcodes, retry-redenering.
- **MSI-fouten**: bij een mislukte `.msi`-installatie wordt het volledige `msiexec`-verbose-log bewaard naast het sessielog, voor verdere troubleshooting.
