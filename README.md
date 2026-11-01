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
- Engine yt-dlp, FFmpeg e FFprobe em `tools/Engine/Bin` durante o desenvolvimento (copiados para `Engine/Bin` na publicação) ou em um diretório configurado por `GETIT_ENGINE_BIN`. Os binários não são versionados; o script do instalador interrompe a geração se os componentes obrigatórios estiverem ausentes.

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
4. Se necessário, valide a engine externa em `tools/Engine/Bin`.

## Compilação

```powershell
dotnet restore GetIt.sln -p:Platform=x64
dotnet build GetIt.sln -p:Platform=x64 --configuration Release
```

## Testes

```powershell
dotnet test GetIt.sln -p:Platform=x64 --configuration Release --logger "console;verbosity=minimal"
```

Os testes de configurações e histórico usam uma pasta temporária isolada e não alteram `%APPDATA%\GetIt`. Os testes de integração reais com FFmpeg são ignorados por padrão; defina `GETIT_FFMPEG_TEST_ENGINE` somente quando quiser executá-los com uma engine local.

## Publicação e distribuição

A solução está preparada para builds reproducíveis com suporte a publicação Windows e geração de artefatos para distribuição. O pipeline de CI em `.github/workflows/ci.yml` realiza:

- restore
- build
- testes
- validação de formatação estática

Para empacotamento e distribuição, `deploy/installer/` contém os scripts do instalador. Os binários da engine não são incluídos no repositório, portanto um clone limpo precisa recebê-los separadamente antes do empacotamento.

## Arquitetura e manutenção

- `GetIt.App`: interface, interação do usuário, telas e fluxo principal
- `GetIt.Core`: lógica compartilhada, configurações e serviços de infraestrutura
- `GetIt.Tests`: testes automatizados para os fluxos críticos
- `tools/Engine/Bin`: binários locais e ignorados pelo Git, copiados para o diretório `Engine/Bin` na publicação
- `docs`: documentação operativa e de estrutura do produto

## Observações importantes

- O histórico e as configurações ficam em `%APPDATA%\GetIt`. As gravações usam arquivos temporários no mesmo diretório e substituição atômica; após uma atualização, uma cópia recuperável da versão anterior é mantida. JSON inválido não é substituído por valores vazios: o GetIt tenta recuperar uma cópia local e informa quando não é possível.
- O atualizador consulta releases estáveis do GitHub e só inicia o instalador após validar o SHA-256 publicado pela API. Esse hash confirma a correspondência com o asset publicado, mas não substitui uma assinatura Authenticode nem protege contra comprometimento da conta ou do processo de publicação; o instalador atualmente publicado não possui assinatura Authenticode.
- O projeto foi estruturado para manter comportamento funcional existente enquanto melhora organização, consistência e manutenção.
- Não há reescrita de negócio ou introdução de camadas artificiais; os ajustes foram feitos para deixar o repositório profissional e evolutivo sem afetar as funcionalidades do cliente.
- O build e os testes devem ser executados em ambiente Windows, com plataforma x64, para manter compatibilidade com WinUI 3.
