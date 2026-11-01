# GetIt

GetIt é um aplicativo Windows para download de mídia, com foco em confiabilidade, boa experiência de uso e estrutura de manutenção profissional. O produto combina interface WinUI 3, downloads via yt-dlp e gestão de fila, histórico e configurações em uma solução que foi preparada para evolução contínua e distribuição.

## Visão do produto

- Download de vídeos e áudios em MP3, WAV e FLAC, com qualidade configurável para MP3
- Suporte a múltiplos links em fila
- Histórico local de downloads concluídos
- Configuração de destino, tema, comportamento e atualizações automáticas do aplicativo e do yt-dlp
- Atualização e manutenção mais fáceis por meio de separação de responsabilidades

## Requisitos

- Windows 10/11
- .NET 10 LTS SDK
- Visual Studio 2022 ou Rider
- Windows App SDK / WinUI 3 habilitados para o projeto
- Engine yt-dlp, FFmpeg e FFprobe em `Engine/Bin` ou em um diretório configurado por `GETIT_ENGINE_BIN`. Se algum componente estiver ausente ou indisponível, o GetIt ainda abre e informa como corrigir a engine.

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
├── Engine/
├── installer/
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
4. Se necessário, valide a engine externa em `Engine/Bin`.

## Compilação

```powershell
dotnet restore GetIt.sln -p:Platform=x64
dotnet build GetIt.sln -p:Platform=x64 --configuration Release
```

## Testes

```powershell
dotnet test GetIt.sln -p:Platform=x64 --configuration Release --logger "console;verbosity=minimal"
```

## Publicação e distribuição

A solução está preparada para builds reproducíveis com suporte a publicação Windows e geração de artefatos para distribuição. O pipeline de CI em `.github/workflows/ci.yml` realiza:

- restore
- build
- testes
- validação de formatação estática

Para empacotamento e distribuição, a pasta `installer/` contém scripts de preparação do instalador e artefatos complementares.

## Arquitetura e manutenção

- `GetIt.App`: interface, interação do usuário, telas e fluxo principal
- `GetIt.Core`: lógica compartilhada, configurações e serviços de infraestrutura
- `GetIt.Tests`: testes automatizados para os fluxos críticos
- `Engine`: binários e dependências externas do runtime de download
- `docs`: documentação operativa e de estrutura do produto

## Observações importantes

- O histórico e as configurações ficam em `%APPDATA%\GetIt`. As gravações usam arquivos temporários no mesmo diretório e substituição atômica; após uma atualização, uma cópia recuperável da versão anterior é mantida. JSON inválido não é substituído por valores vazios: o GetIt tenta recuperar uma cópia local e informa quando não é possível.
- O atualizador consulta releases estáveis do GitHub e só inicia o instalador após validar o SHA-256 publicado pela API. Esse hash confirma a correspondência com o asset publicado, mas não substitui uma assinatura Authenticode nem protege contra comprometimento da conta ou do processo de publicação; o instalador atualmente publicado não possui assinatura Authenticode.
- O projeto foi estruturado para manter comportamento funcional existente enquanto melhora organização, consistência e manutenção.
- Não há reescrita de negócio ou introdução de camadas artificiais; os ajustes foram feitos para deixar o repositório profissional e evolutivo sem afetar as funcionalidades do cliente.
- O build e os testes devem ser executados em ambiente Windows, com plataforma x64, para manter compatibilidade com WinUI 3.
