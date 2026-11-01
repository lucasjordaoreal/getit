# GetIt — avisos de componentes de terceiros

**Composição do instalador 0.1.2:** os avisos abaixo descrevem os componentes empacotados. FFmpeg/FFprobe e Deno não estão no instalador; após consentimento, o aplicativo pode obtê-los diretamente dos releases fixos dos fornecedores, sem hospedar ou incluir cópias no pacote GetIt.

Este documento separa os binários locais da composição planejada para o instalador. Os hashes identificam os arquivos locais examinados; não substituem assinatura digital nem demonstram, por si sós, a origem de um binário. Os textos de licença preservados estão em [`texts/`](./texts/).

## Engine local examinada (não incluída na nova composição planejada)

Os executáveis abaixo continuam na pasta local de desenvolvimento e em artefatos anteriores. A nova publicação exclui FFmpeg, FFprobe, FFplay, Node.js e Deno. Após consentimento explícito, a configuração automática pode baixar FFmpeg/FFprobe e Deno diretamente das URLs oficiais descritas neste documento; o GetIt valida os hashes e executa smoke tests antes de salvar os caminhos.

| Arquivo na instalação | Versão observada | SHA-256 | Origem e licença identificadas | Situação |
|---|---:|---|---|---|
| `Engine/Bin/ffmpeg.exe` | FFmpeg 8.1, `essentials_build-www.gyan.dev` | `1A65D5B0B10D8D9A81D2824A3538046A40ED3607C906B335A166ADD87613F705` | Build estática da Gyan; a página do fornecedor classifica suas builds como GPLv3. | **Não redistribuir:** a correspondência das fontes permanece sem comprovação; a nova publicação deve excluir o arquivo. |
| `Engine/Bin/ffprobe.exe` | FFmpeg 8.1, `essentials_build-www.gyan.dev` | `54B944D6095C4588D7548424D7ACD5118390A9D4F01B1C92F841547FC9A7429C` | Distribuído no mesmo pacote da Gyan; configuração GPLv3 e bibliotecas estáticas equivalentes às do FFmpeg. | **Não redistribuir:** a nova publicação deve excluir o arquivo e usar uma engine externa configurada pelo usuário. |
| `Engine/Bin/ffplay.exe` | FFmpeg 8.1, `essentials_build-www.gyan.dev` | `BA1B492EC6F784F4DD72813C7CD886BB3ABAF11647DB95ACF95A7E35E6A9C1A1` | Distribuído no mesmo pacote da Gyan; build estática GPLv3. | **Não redistribuir:** além da pendência de fontes, não foram encontradas chamadas de FFplay nos fluxos do GetIt. |
| `Engine/Bin/yt-dlp.exe` | 2026.08.19 (Python 3.10.11) | `66674953FE251B89F4D08C5F0E35E0728679BD67AB3D7D05C0562AF101DD3E7A` | Release oficial `yt-dlp/yt-dlp`, tag `2026.08.19`; hash coincide com o asset oficial `yt-dlp.exe`. O agregado de licenças de terceiros está embutido no executável PyInstaller. | **Documentação obtida:** `texts/yt-dlp-2026.08.19-THIRD-PARTY-LICENSES.txt` foi extraído do executável; SHA-256 `5B7BC105C562B81B90D1A2CD5A8CC8B336EE107AE0AE81CE58450CD8B1BC2AD4`. O arquivo inclui licenças de Python e informa uma oferta de fontes pelos mantenedores de yt-dlp. |
| `Engine/Bin/node.exe` | Node.js 20.11.1 | `BC585910690318AAEBE3C57669CB83CA9D1E5791EFD63195E238F54686E6C2EC` | O hash coincide com `win-x64/node.exe` no `SHASUMS256.txt` oficial de Node.js 20.11.1. Licença MIT; o arquivo `LICENSE` dessa tag também contém avisos de componentes de terceiros do Node. A linha 20 está EOL desde março de 2026. O GetIt só acrescenta a pasta ao `PATH`; não foi encontrada seleção explícita `--js-runtimes node`. A documentação atual do yt-dlp requer Node 22+ e configuração explícita. | **VALIDADO** para o binário e texto de licença identificados; não selecionado pelo fluxo observado e abaixo do mínimo atual se Node vier a ser habilitado. |
| `Engine/Bin/deno.exe` | Deno 2.7.1 (V8 14.5.201.2-rusty; TypeScript 5.9.2) | `6C35A4C45D521B340E5CE78DFB25014E5B04894067B5E71C1180273FFA5B3792` | A release oficial `v2.7.1` (commit `1df618d969894353731981a17784816167ad82fb`) declara MIT para Deno. O SHA-256 oficial do ZIP x64, `94d71d4772436de27a0495933ca4bab7b6895992622b65baeaf4b7995dae1e69`, foi verificado; o `deno.exe` extraído tem o mesmo SHA-256 do arquivo local. | **Não incluído no instalador:** após consentimento, o app baixa o ZIP oficial direto do release e verifica o checksum antes de extrair. A release contém somente `deno.exe`, sem notices completas das dependências Rust/V8; isso é divulgado antes do download e não é apresentado como isenção ou aprovação jurídica. |
| `Engine/Bin/help.txt` | Snapshot de ajuda do yt-dlp; sem versão própria | `FCD5E488D6EC52A8A71C9A10DEC7F6B6BE48BDE37A6F169A6990B686165264C9` | Arquivo local auxiliar; não é necessário para executar o yt-dlp. | **Não redistribuído:** o processo de geração não foi registrado e o app não o utiliza. |

## Build candidata (não distribuída)

A candidata BtbN foi validada em testes técnicos, mas **não está instalada nem incluída como engine atual**. Não copie seus dados para a tabela de binários distribuídos até a aprovação de substituição.

| Asset candidato | Identificação | Situação de redistribuição |
|---|---|---|
| `ffmpeg-n8.1.3-27-g8dca1586f4-win64-gpl-8.1.zip` | Release fixa `autobuild-2026-10-31-12-59`; SHA-256 `401dffcf14c301082e1fc03b0bc876b92ea676bfc521fc56e80a1537d137034f`; commit FFmpeg `8dca1586f468bfcbb78ae3ed618af1361d9f140a`; scripts BtbN `5031c6f1082414e53688d39e521849905071070d`. | **Não incluída no instalador:** o app poderá baixar diretamente do release oficial somente após consentimento e verificação do SHA-256. A build é GPLv3. O material público identificado não contém pacote completo de fontes correspondentes nem manifesto atestado das dependências efetivamente ligadas; por isso, o fluxo não declara a obrigação de fontes resolvida nem a distribuição juridicamente aprovada. |

O relatório das evidências, hashes dos snapshots obtidos, pendências e pedido técnico ao mantenedor está em [`THIRD_PARTY_SOURCES.md`](./THIRD_PARTY_SOURCES.md). A presença pública de scripts e commits de dependências não prova, por si só, a correspondência com esse binário.

### FFmpeg: configuração observada

`ffmpeg.exe -version`, `ffprobe.exe -version` e `ffmpeg.exe -buildconf` foram executados sem abrir ou processar mídias. O banner informa:

- `ffmpeg version 8.1-essentials_build-www.gyan.dev`
- compilador GCC 15.2.0 (Rev11, MSYS2)
- `--enable-gpl`, `--enable-version3` e `--enable-static`
- bibliotecas habilitadas: Cairo, Fontconfig, Iconv, GnuTLS, libxml2, GMP, bzip2, LZMA, zlib, SRT, SSH, ZeroMQ, AviSynth+, SDL2, WebP, x264, x265, Xvid, AOM, OpenJPEG, VPx, Media Foundation, ASS, FreeType, FriBidi, HarfBuzz, vid.stab, VMAF, zimg, AMF, CUDA/NVIDIA, libvpl, OpenAL, Game Music Emu, OpenMPT, Opencore AMR-NB/AMR-WB, MP3 LAME, Theora, vo-amrwbenc, GSM, Opus, Speex, Vorbis e Rubber Band.

Como a configuração é estática e habilita componentes GPL, o conjunto não pode ser tratado como uma build LGPL simples. `-buildconf` não informa versões exatas, hashes, patches ou arquivos-fonte das bibliotecas externas; esses dados continuam necessários. Não foram encontrados DLLs na pasta `Engine/Bin`.

A página da Gyan oferece a release `8.1` e o pacote `ffmpeg-8.1-essentials_build.7z`, consistente com o identificador do banner. O arquivo local não foi comparado byte a byte com o conteúdo extraído desse pacote. A build reporta a versão `8.1`, mas não apresenta um commit/revision Git que prove a correspondência exata com a tag de fonte FFmpeg `n8.1`.

## Componentes da publicação anterior examinada

Os seguintes executáveis também aparecem na publicação self-contained x64, em `Libs/`:

| Arquivo | Versão observada | SHA-256 | Origem/licença |
|---|---|---|---|
| `Libs/createdump.exe` | .NET 10.0.12; commit informado no recurso do arquivo `95017c711e6afc1085133d440e42b4bd78155701` | `315307159925D33EB604186A512901D080ECFC4CD8B8198030A7D398C6AF2B0B` | Ferramenta do runtime .NET self-contained; licença MIT e avisos oficiais incluídos em `texts/dotnet-runtime-*`. |
| `Libs/RestartAgent.exe` | 2.0 | `9ED20B0F642269430584E97CD64E1CA9D67D344CD79298A49FD752F78567798B` | Componente de runtime Windows App SDK distribuído pelo pacote NuGet Microsoft.WindowsAppSDK 2.1.3; avisos Microsoft correspondentes incluídos em `texts/`. |

A publicação anterior também contém assemblies/runtime self-contained do .NET 10.0.12, Windows App SDK e dependências NuGet. Os manifestos/licenças correspondentes foram copiados das versões resolvidas:

- `CommunityToolkit.Mvvm` 8.4.2 — MIT e avisos de terceiros.
- `Microsoft.Web.WebView2` 1.0.3719.77 — textos `LICENSE.txt` e `NOTICE.txt` do pacote.
- `Microsoft.WindowsAppSDK` 2.1.3 e dependências resolvidas (`AI` 2.1.10, `Base` 2.0.4, `DWrite` 2.1.0, `Foundation` 2.0.21, `InteractiveExperiences` 2.0.13, `ML` 2.1.1, `Runtime` 2.1.3, `Widgets` 2.0.5 e `WinUI` 2.1.0) — licenças e avisos dos pacotes preservados em `texts/`.
- `System.Numerics.Tensors` 9.0.0 — MIT e avisos de terceiros.
- Runtime .NET 10.0.12 — MIT e avisos oficiais preservados em `texts/dotnet-runtime-*`.

## Composição proposta para a publicação 0.1.2

- Somente `yt-dlp.exe` é incluído da pasta de engine; Deno, FFmpeg, FFprobe, FFplay e Node.js são explicitamente excluídos.
- Após consentimento explícito, o assistente pode obter FFmpeg/FFprobe e Deno diretamente das releases fixas listadas acima para `%LOCALAPPDATA%\GetIt\Tools`. A verificação do hash antecede extração e execução; os avisos/informações de licença e lacunas de fontes são mostrados antes do aceite.
- O yt-dlp usa a pasta configurada de FFmpeg/FFprobe. O app valida versão, correspondência entre executáveis e encoders necessários; instalações manuais continuam disponíveis.
- Para extratores do YouTube que precisam de JavaScript, o assistente configura Deno 2.7.1. O caminho só é acrescentado ao ambiente dos processos filhos do yt-dlp; o PATH global não é alterado. O executável oficial PyInstaller do yt-dlp inclui EJS, então o app não obtém scripts EJS separados.
- O pacote oficial de avisos de yt-dlp foi extraído do EXE de hash identificado acima. Deno, Node.js e `help.txt` não são copiados.
- O instalador não inclui Deno. A release usada declara MIT para Deno, mas não fornece notices completos das dependências Rust/V8; a licença MIT não é apresentada como inventário suficiente do binário. O usuário é informado dessa limitação antes do download.
- Os quatro textos de licença FFmpeg existentes são materiais de auditoria/localidade e não substituem fontes correspondentes. A nova publicação não deverá incluir os binários Gyan nem declarar a conformidade deles.

`Microsoft.Windows.SDK.BuildTools` 10.0.26100.4654 e Inno Setup 6.7.3 foram usados como ferramentas de build; não foram identificados como executáveis do aplicativo instalado. O app não inclui essas ferramentas como dependências de runtime.

## Acesso após a instalação

Os documentos de terceiros selecionados pelo projeto são publicados em `{app}\Licenses`. Na página **Configurações > Sobre**, o botão **Licenças de terceiros** abre essa pasta. O gate do instalador verifica a presença dos documentos exigidos para todos os componentes redistribuídos.

## Referências oficiais

- [Considerações legais do FFmpeg](https://ffmpeg.org/legal.html)
- [Builds Windows da Gyan](https://www.gyan.dev/ffmpeg/builds/)
- [Release Gyan FFmpeg 8.1](https://github.com/GyanD/codexffmpeg/releases/tag/8.1)
- [Fonte FFmpeg tag n8.1](https://github.com/FFmpeg/FFmpeg/tree/n8.1)
- [Licença yt-dlp 2026.08.19](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/LICENSE)
- [Licença Deno v2.7.1](https://github.com/denoland/deno/blob/v2.7.1/LICENSE.md)
- [Release Deno v2.7.1](https://github.com/denoland/deno/releases/tag/v2.7.1)
- [SHA-256 oficial do asset Deno Windows x64](https://github.com/denoland/deno/releases/download/v2.7.1/deno-x86_64-pc-windows-msvc.zip.sha256sum)
- [Licença Node.js v20.11.1](https://github.com/nodejs/node/blob/v20.11.1/LICENSE)
- [Checksums Node.js v20.11.1](https://nodejs.org/dist/v20.11.1/SHASUMS256.txt)
- [yt-dlp: dependências JavaScript e runtimes](https://github.com/yt-dlp/yt-dlp/wiki/EJS)
- [BtbN: release fixa avaliada](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-10-31-12-59)
- [BtbN: scripts de build no commit avaliado](https://github.com/BtbN/FFmpeg-Builds/tree/5031c6f1082414e53688d39e521849905071070d)
- [BtbN: suporte de Windows e variantes](https://github.com/BtbN/FFmpeg-Builds#ffmpeg-static-auto-builds)
- [FFmpeg: commit avaliado](https://github.com/FFmpeg/FFmpeg/tree/8dca1586f468bfcbb78ae3ed618af1361d9f140a)
- [Windows 10: release information e lifecycle](https://learn.microsoft.com/en-us/windows/release-health/release-information)
- [.NET: versões Windows suportadas](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [WebView2: distribuição do Runtime](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [Licença e avisos do .NET Runtime 10.0.12](https://github.com/dotnet/runtime/tree/v10.0.12)
