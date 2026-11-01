# GetIt

GetIt é um aplicativo Windows para download de mídia, com foco em confiabilidade, boa experiência de uso e estrutura de manutenção profissional. O produto combina interface WinUI 3, downloads via yt-dlp e gestão de fila, histórico e configurações em uma solução que foi preparada para evolução contínua e distribuição.

## Visão do produto

- Download de vídeos e áudios em MP3, WAV e FLAC, com qualidade configurável para MP3
- Suporte a múltiplos links em fila
- Prévia de até 50 itens de playlists compatíveis com yt-dlp, com seleção individual antes de adicionar vídeos à fila
- Presets para novos itens e personalização de formato e qualidade por download
- Histórico local de downloads concluídos
- Configuração de destino, tema, comportamento e atualizações automáticas do aplicativo e do yt-dlp
- Atualização e manutenção mais fáceis por meio de separação de responsabilidades

## Requisitos

- Windows 10/11
- .NET 10 LTS SDK
- Visual Studio 2022 ou Rider
- Windows App SDK / WinUI 3 habilitados para o projeto
- Engine `yt-dlp.exe` em `tools/Engine/Bin` durante o desenvolvimento. No aplicativo instalado, a configuração inicial pode baixar FFmpeg/FFprobe e Deno diretamente de releases fixas oficiais, após consentimento e validação de SHA-256. Também é possível apontar para instalações existentes em **Configurações**.

## Estrutura do repositório

```text
GetIt/
├── .github/
│   └── workflows/
│       └── ci.yml
├── docs/
│   └── PROJECT_STRUCTURE.md
├── src/
│   ├── GetIt.App/
│   ├── GetIt.Core/
│   └── ...
├── tests/
│   └── GetIt.Tests/
├── tools/
│   └── Engine/Bin/          # Binários locais, ignorados pelo Git
├── deploy/
│   └── installer/
├── .gitignore
├── Directory.Build.props
├── GetIt.sln
├── README.md
├── RELEASE.MD
└── ...
```

## Configuração de desenvolvimento

1. Clone o repositório.
2. Abra a solução `GetIt.sln` no Visual Studio ou Rider.
3. Restore as dependências com `dotnet restore GetIt.sln -p:Platform=x64`.
4. Disponibilize `yt-dlp.exe` em `tools/Engine/Bin`.
5. Instale FFmpeg/FFprobe de uma fonte confiável e configure a pasta em **Configurações > Engine de mídia**; a validação verifica versões e os encoders exigidos pelo GetIt.
6. Para extratores do YouTube que usam JavaScript, instale Deno 2.3.0 ou posterior e configure a pasta em **Configurações > Runtime JavaScript para YouTube**.

## Compilação

```powershell
dotnet restore GetIt.sln -p:Platform=x64
dotnet build GetIt.sln -p:Platform=x64 --configuration Release
```

## Testes

```powershell
dotnet test GetIt.sln -p:Platform=x64 --configuration Release --logger "console;verbosity=minimal"
```

Os testes de configurações e histórico usam uma pasta temporária isolada e não alteram `%APPDATA%\GetIt`. Os testes de integração de FFmpeg e Deno são ignorados por padrão. Para os testes de engines locais, defina `GETIT_FFMPEG_TEST_ENGINE` e `GETIT_DENO_TEST_ENGINE`; para habilitar o teste do fluxo de configuração com os arquivos oficiais já baixados, defina também `GETIT_DEPENDENCY_SETUP_ASSET_DIR`. Os testes verificam novamente os SHA-256 configurados e não baixam mídia.

## Publicação e distribuição

A solução está preparada para builds reproducíveis com suporte a publicação Windows e geração de artefatos para distribuição. O pipeline de CI em `.github/workflows/ci.yml` realiza:

- restore
- build
- testes
- validação de formatação estática

Para empacotamento e distribuição, `deploy/installer/` contém os scripts do instalador. Os binários da engine não são incluídos no repositório, portanto um clone limpo precisa receber `yt-dlp.exe` separadamente. O instalador inclui somente o `yt-dlp.exe` empacotado; FFmpeg, FFprobe, FFplay, Node.js e Deno são excluídos. O aplicativo pode baixar FFmpeg/FFprobe e Deno diretamente dos releases oficiais fixos após consentimento explícito, para `%LOCALAPPDATA%\GetIt\Tools`, verificando SHA-256 antes de extrair e executar. Isso não constitui aprovação jurídica das condições do fornecedor.

Os avisos e textos de licença ficam em `licenses/` e são copiados para `Licenses/` na publicação; a pasta também pode conter textos de licença arquivados de runtimes externos, o que não significa que seus executáveis estejam no pacote. A página **Configurações > Sobre** abre essa pasta. Consulte [`licenses/THIRD_PARTY_NOTICES.md`](licenses/THIRD_PARTY_NOTICES.md) e [`licenses/THIRD_PARTY_SOURCES.md`](licenses/THIRD_PARTY_SOURCES.md) para as versões, origens, hashes, licenças declaradas e pendências dos componentes de download opcional.

### Atualização de 0.1.1 para 0.1.2

O instalador preserva as configurações, a fila e o histórico em `%APPDATA%\GetIt`, mas remove de `{app}\Engine\Bin` as cópias antigas de FFmpeg, FFprobe, FFplay, Deno e Node.js. Ao abrir a versão 0.1.2 sem as dependências válidas, o app oferece a configuração automática; se preferir, as pastas podem ser selecionadas manualmente. Os executáveis antigos removidos não apagam arquivos pessoais nem a fila salva.

## Arquitetura e manutenção

- `GetIt.App`: interface, interação do usuário, telas e fluxo principal
- `GetIt.Core`: lógica compartilhada, configurações e serviços de infraestrutura
- `GetIt.Tests`: testes automatizados para os fluxos críticos
- `tools/Engine/Bin`: `yt-dlp.exe` local, ignorado pelo Git e copiado para a publicação; FFmpeg/FFprobe e Deno são externos ou baixados diretamente pelo fluxo consentido
- `docs`: documentação operativa e de estrutura do produto

## Observações importantes

- O histórico e as configurações ficam em `%APPDATA%\GetIt`. As gravações usam arquivos temporários no mesmo diretório e substituição atômica; após uma atualização, uma cópia recuperável da versão anterior é mantida. JSON inválido não é substituído por valores vazios: o GetIt tenta recuperar uma cópia local e informa quando não é possível.
- O atualizador consulta releases estáveis do GitHub e só inicia o instalador após validar o SHA-256 publicado pela API. Esse hash confirma a correspondência com o asset publicado, mas não substitui uma assinatura Authenticode nem protege contra comprometimento da conta ou do processo de publicação; o instalador atualmente publicado não possui assinatura Authenticode.
- O projeto foi estruturado para manter comportamento funcional existente enquanto melhora organização, consistência e manutenção.
- Não há reescrita de negócio ou introdução de camadas artificiais; os ajustes foram feitos para deixar o repositório profissional e evolutivo sem afetar as funcionalidades do cliente.
- O build e os testes devem ser executados em ambiente Windows, com plataforma x64, para manter compatibilidade com WinUI 3.
