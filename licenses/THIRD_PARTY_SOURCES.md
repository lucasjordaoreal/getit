# GetIt — fontes correspondentes e gate de redistribuição

**Composição planejada para 0.1.2:** somente `yt-dlp.exe` é distribuído dentre os runtimes de mídia/JS. FFmpeg/FFprobe e Deno são configurados externamente; FFplay e Node não são distribuídos. Assim, os avisos-fontes das builds FFmpeg e das dependências Rust/V8 de Deno não são usados como justificativa para redistribuir esses executáveis. O pacote de licenças oficial incorporado ao yt-dlp foi extraído e preservado.

Esta mudança de composição não declara conformidade jurídica geral nem valida a licença da build que cada usuário escolher para FFmpeg. O usuário deve obter a engine externa de uma fonte confiável e cumprir as condições indicadas pelo respectivo fornecedor.

## Gyan existente (não redistribuída)

O binário declara FFmpeg `8.1-essentials_build-www.gyan.dev`, `--enable-gpl`, `--enable-version3` e linkagem estática. A fonte do FFmpeg `n8.1` está disponível em [FFmpeg/FFmpeg, tag n8.1](https://github.com/FFmpeg/FFmpeg/tree/n8.1), e a build que parece corresponder ao banner é o asset [`ffmpeg-8.1-essentials_build.7z`](https://github.com/GyanD/codexffmpeg/releases/download/8.1/ffmpeg-8.1-essentials_build.7z).

Essas referências não provam que a tag, isoladamente, corresponda aos binários. A release da Gyan não oferece, nos assets verificados, um pacote de código-fonte correspondente. Para fechar a pendência é preciso obter e conferir:

1. Fonte exata de FFmpeg usada para os três executáveis, com revisão/commit confirmado.
2. Fontes e revisões das bibliotecas externas realmente incorporadas estaticamente, incluindo as habilitadas na configuração observada.
3. Scripts/receitas e ferramentas de build da Gyan necessários para reproduzir a configuração.
4. Patches/diffs aplicados, ou evidência verificável de que não houve alterações.
5. Configuração completa, proveniência e avisos/licenças de cada componente incorporado.
6. Um manifesto com URLs de origem e SHA-256 de todos os arquivos no pacote de fontes.

O texto de `-buildconf`, o repositório de código do FFmpeg atual e o asset binário da Gyan não substituem esses materiais. Não foi inventado nem produzido código-fonte, patch ou receita de build nesta etapa.

## Candidata BtbN GPL (não distribuída)

A build foi validada funcionalmente em testes sintéticos e nos fluxos de conversão já registrados na Etapa 13C, em Windows 11. Isso não altera a engine Gyan instalada e não demonstra conformidade para redistribuição.

| Evidência | Resultado |
|---|---|
| Release/asset selecionado | `autobuild-2026-10-31-12-59` / `ffmpeg-n8.1.3-27-g8dca1586f4-win64-gpl-8.1.zip` |
| SHA-256 do ZIP oficial | `401dffcf14c301082e1fc03b0bc876b92ea676bfc521fc56e80a1537d137034f` |
| Commit FFmpeg indicado pelo binário | `8dca1586f468bfcbb78ae3ed618af1361d9f140a` |
| Commit do repositório de build | `5031c6f1082414e53688d39e521849905071070d` |
| Registro do workflow | Execução `41199749965` concluiu com sucesso usando o commit de scripts acima; o registro não é um manifesto de fontes anexado ao asset |
| Snapshot de fontes FFmpeg obtido | 23.135.032 bytes; SHA-256 `742046ABE27A5F013DA293BBE878AAA47A163A6156253BF46A14D0F858A232E7` |
| Snapshot de scripts BtbN obtido | 259.670 bytes; SHA-256 `8BB365E8747BA70961F179791CBC225B05E474BBEB861FDDB4420677932B22CA` |

O binário declara FFmpeg `n8.1.3-27-g8dca1586f4-20261031`, configuração estática com GPLv3/version3 e contém um texto GPLv3, mas não código-fonte nem DLLs. A versão curta no banner coincide com o prefixo do commit completo resolvido e o snapshot desse commit foi obtido. Isso identifica a fonte FFmpeg com boa rastreabilidade, mas não é uma atestação independente de quais fontes e patches foram efetivamente usados na execução do build.

O snapshot BtbN inclui variantes, scripts, recipes e patches. Por exemplo, as recipes registram x264 `0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee`, OpenH264 `1bca62311e447fd9c39fc93de2c4b96cd5ce971c` e LAME SVN revision `7113`; o repositório também contém patches para AOM e ARIBB24. Esses registros são evidência útil de intenção de build, mas não substituem um manifesto atestado que relacione cada dependência e patch àquele asset. As fontes dessas bibliotecas não foram reunidas neste pacote de auditoria.

### Materiais da candidata que não serão redistribuídos

1. Manifesto específico do asset com todas as bibliotecas, fontes, revisões, hashes, licenças, patches e avisos.
2. Fontes completas correspondentes e configuração/logs efetivos do build.
3. Ambiente de compilação imutável e sua proveniência; os registros consultados usam referências mutáveis (`latest`) e não fecham a revisão dos pacotes de build.

Esses materiais continuam necessários antes de qualquer futura redistribuição da BtbN, mas não bloqueiam uma publicação que comprovadamente exclua seus binários. **Não foi criado** `GetIt-Third-Party-Sources-v0.1.2.zip`; os snapshots de investigação não são um pacote de fontes correspondente.

### Solicitação técnica sugerida à BtbN

```text
Subject: Source and build-provenance materials for the fixed FFmpeg 8.1 GPL Windows asset

Hello,

We are evaluating redistribution of this exact asset in a Windows application:

- Release: autobuild-2026-10-31-12-59
- Asset: ffmpeg-n8.1.3-27-g8dca1586f4-win64-gpl-8.1.zip
- SHA-256: 401dffcf14c301082e1fc03b0bc876b92ea676bfc521fc56e80a1537d137034f
- FFmpeg commit identified by the binary: 8dca1586f468bfcbb78ae3ed618af1361d9f140a
- Build scripts commit: 5031c6f1082414e53688d39e521849905071070d
- Workflow run we identified: 41199749965

Could you provide or point us to the source materials corresponding to this specific asset, including:

1. A complete manifest/SBOM of every library statically incorporated, with upstream URL, exact commit or release, source archive hash, license, and required notices.
2. The complete corresponding source archives, including transitive dependencies where required.
3. The exact patches applied to FFmpeg and each dependency, with hashes.
4. The effective configure/build commands and logs for this asset.
5. The resolved versions/digests of the runner, Docker/base images, compiler/toolchain, and build packages, or the available records if these were not pinned.
6. Confirmation of which source offer and accompanying notices are intended to satisfy the applicable GPLv3 obligations for this release.

The public source commit and build recipes are useful, but we cannot infer from them alone which dependency revisions and patches were present in this particular binary. We will not redistribute the asset until the corresponding materials can be verified.

Thank you.
```

### Compatibilidade Windows — decisão ainda pendente

- A documentação da BtbN para a build Windows selecionada estabelece Windows 10 22H2 (build 19045) ou posterior como mínimo suportado. A execução funcional conhecida para o GetIt ocorreu em Windows 11; não houve teste em VM Windows 10 22H2.
- O `GetIt.App.csproj` declara `TargetPlatformMinVersion=10.0.17763.0`; `Package.appxmanifest` também usa 17763. Como o app é publicado unpackaged (`WindowsPackageType=None`), esse manifesto MSIX não é, sozinho, uma barreira de instalação do Inno Setup. O `app.manifest` declara compatibilidade geral com Windows 10, não um build mínimo.
- O instalador Inno Setup não declara `MinVersion`, e o atualizador não filtra releases por versão do Windows. A versão 0.1.1 pode oferecer o instalador 0.1.2 antes de qualquer rejeição do sistema.
- A documentação oficial do .NET 10 consultada enumera Windows 11 e versões/edições Windows 10 LTSC/Enterprise específicas; Windows 10 22H2 geral não aparece como configuração suportada. Assim, não há atualmente um piso Windows 10 comum que esteja documentado como suportado tanto pelo .NET 10 quanto pela build BtbN selecionada. Windows 10 22H2 pode funcionar, mas isso não foi comprovado como configuração suportada pelo conjunto GetIt/.NET/Windows App SDK.
- O projeto usa `Microsoft.WindowsAppSDK` 2.1.3; a documentação oficial de release consultada era da linha 1.8, não dessa versão. Portanto, ela não comprova o piso exato do pacote usado pelo GetIt.
- Windows 10 1809 LTSC e 21H2 LTSC podem continuar dentro de ciclos de suporte Microsoft próprios, mas ficam abaixo do mínimo declarado pela BtbN. Se o produto adotar 22H2 como mínimo, essas edições deixarão de ser aceitas pelo GetIt, mesmo que o ciclo de vida Microsoft ainda esteja ativo. Windows 10 22H2 Home/Pro/Enterprise geral encerrou o suporte regular em 14/10/2025; a continuidade de atualizações depende de ESU/edição e não deve ser presumida para todos os usuários.
- O WebView2 Evergreen precisa estar instalado antes da criação do controle. Windows 11 o inclui; a instalação atual não foi confirmada como instaladora do runtime em máquinas Windows 10 limpas. O pacote NuGet do SDK/loader não prova que o runtime Evergreen esteja presente.

**Recomendação atual:** não declarar Windows 10 22H2 como requisito mínimo definitivo ainda. Para a BtbN fixa, 19045 é o mínimo documentado pelo fornecedor do FFmpeg, mas o suporte de .NET 10 e o comportamento completo do app nessa versão permanecem pendentes. Se for necessário declarar uma plataforma formalmente suportada imediatamente, Windows 11 é a interseção mais segura das evidências atuais. Se 22H2 for aprovado como requisito de produto apesar dessa ressalva, validar em VM limpa (incluindo WebView2, instalação, execução, conversão e atualização) e documentar o limite de suporte/ESU.

Uma adoção futura exigiria avaliar, em conjunto, `GetIt.App.csproj`, `Package.appxmanifest`, `GetItInstaller.iss`, uma verificação antes da inicialização de WinUI, a UX do atualizador, README/release notes e testes de instalação/upgrade. A verificação atual do atualizador não impede a oferta da atualização; o instalador precisa rejeitar sistemas abaixo do piso com mensagem clara. Nenhuma alteração nesta tarefa modificou o requisito mínimo do Windows.

## Deno 2.7.1 — procedência e decisão de distribuição

- **Identificação e correspondência verificadas:** Deno Land Inc. / `denoland/deno`, tag `v2.7.1` (release de 25/02/2026). O executável local e o executável extraído do ZIP oficial x64 apresentam SHA-256 idêntico: `6C35A4C45D521B340E5CE78DFB25014E5B04894067B5E71C1180273FFA5B3792`. O ZIP `deno-x86_64-pc-windows-msvc.zip` foi obtido da release fixa e validado pelo digest oficial `94D71D4772436DE27A0495933CA4BAB7B6895992622B65BAEAF4B7995DAE1E69`; o arquivo `.sha256sum` oficial reporta o mesmo valor. `deno.exe --version` retorna 2.7.1, V8 14.5.201.2-rusty e TypeScript 5.9.2. A release ZIP contém apenas `deno.exe`, não um arquivo de avisos.
- **Materiais upstream observados:** a tag resolve ao commit `1df618d969894353731981a17784816167ad82fb`, declara MIT e contém `Cargo.lock` (blob Git `b47ff84f6611bd8becfc921900f9ff799f87fd23`). Isso fixa manifestos de crates Rust, mas não é um inventário de licenças das dependências efetivamente ligadas: também é necessário resolver os metadados/licenças dos crates transitivos e documentar V8 e demais componentes C++ empacotados. Não foi encontrado um SBOM/arquivo oficial de notices associado ao asset.
- **Limite de evidência e alternativa adotada:** não é seguro fabricar `Deno-THIRD-PARTY-NOTICES.txt` a partir apenas do `Cargo.lock` nem declarar completa a lista de dependências incorporadas sem verificar V8 e as licenças de cada componente. Por isso, o instalador 0.1.2 não redistribui Deno. O app mantém o suporte: em **Configurações > Runtime JavaScript para YouTube**, o usuário aponta para Deno 2.3.0 ou posterior; o diretório é acrescentado apenas ao PATH do processo yt-dlp. A ausência de Deno não bloqueia outros fluxos, mas extratores que exigem JavaScript precisam do runtime configurado. O MIT da fonte é mantido em `licenses/texts/Deno-MIT-LICENSE.md` e não é tratado como notice suficiente para o binário completo.

## yt-dlp 2026.08.19 — inventário de licença incluído

- O SHA-256 do EXE local coincide com o asset oficial da release. O `THIRD_PARTY_LICENSES.txt` oficial está embutido no executável PyInstaller e foi extraído do EXE; SHA-256 `5B7BC105C562B81B90D1A2CD5A8CC8B336EE107AE0AE81CE58450CD8B1BC2AD4`.
- O documento agrega as licenças de terceiros fornecidas pelos mantenedores, inclusive Python/PyInstaller, e informa que os fontes estão disponíveis nos projetos upstream ou podem ser solicitados a `maintainers@yt-dlp.org`.
- `help.txt` não é copiado para a nova distribuição.

Referências oficiais consultadas: [suporte Windows da BtbN](https://github.com/BtbN/FFmpeg-Builds#ffmpeg-static-auto-builds), [matriz Windows do .NET](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [lifecycle do Windows 10](https://learn.microsoft.com/en-us/windows/release-health/release-information), [release notes Windows App SDK 1.8](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8), [distribuição do WebView2 Runtime](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution), [guia de runtimes JS do yt-dlp](https://github.com/yt-dlp/yt-dlp/wiki/EJS), [runtime installation do Deno](https://docs.deno.com/runtime/getting_started/installation/) e [política de releases do Node.js](https://github.com/nodejs/Release).

## Gate de publicação e conteúdo do pacote

Como FFmpeg/FFprobe/FFplay e Deno não serão redistribuídos, pacotes de fontes/notices dessas engines não fazem parte do gate da nova composição. O script `deploy/installer/build_installer.ps1`:

- exige `yt-dlp.exe` e os avisos/licenças oficiais do yt-dlp, além dos componentes .NET/Windows App SDK efetivamente incluídos;
- verifica que a publicação inclui `yt-dlp.exe` e rejeita FFmpeg, FFprobe, FFplay, Node ou Deno se algum deles aparecer no resultado;
- mantém os avisos e licenças dos runtimes e pacotes .NET/Windows App SDK efetivamente publicados.

Durante a atualização in-place, `GetItInstaller.iss` remove os executáveis FFmpeg/FFprobe/FFplay, Deno e Node que versões anteriores copiavam para `{app}\Engine\Bin`. Preferências e histórico em `%APPDATA%\GetIt` não são removidos; uma configuração antiga de FFmpeg que apontava para a pasta de instalação precisa ser substituída em **Configurações > Engine de mídia**. A decisão não constitui aconselhamento jurídico.
