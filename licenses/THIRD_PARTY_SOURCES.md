# GetIt — fontes correspondentes e gate de redistribuição

**Estado: BLOQUEADOR.** O código FFmpeg `n8.1` está publicamente disponível, mas não foi obtido nem comprovado o conjunto completo correspondente aos executáveis Gyan empacotados. Também faltam a revisão das licenças/notices das dependências compiladas de Deno e do executável congelado do yt-dlp.

## Fonte do FFmpeg

O binário declara FFmpeg `8.1-essentials_build-www.gyan.dev`, `--enable-gpl`, `--enable-version3` e linkagem estática. A fonte do FFmpeg `n8.1` está disponível em [FFmpeg/FFmpeg, tag n8.1](https://github.com/FFmpeg/FFmpeg/tree/n8.1), e a build que parece corresponder ao banner é o asset [`ffmpeg-8.1-essentials_build.7z`](https://github.com/GyanD/codexffmpeg/releases/download/8.1/ffmpeg-8.1-essentials_build.7z).

Essas referências não provam que a tag, isoladamente, corresponda aos binários. A release da Gyan não oferece, nos assets verificados, um pacote de código-fonte correspondente. Para fechar a pendência é preciso obter e conferir:

1. Fonte exata de FFmpeg usada para os três executáveis, com revisão/commit confirmado.
2. Fontes e revisões das bibliotecas externas realmente incorporadas estaticamente, incluindo as habilitadas na configuração observada.
3. Scripts/receitas e ferramentas de build da Gyan necessários para reproduzir a configuração.
4. Patches/diffs aplicados, ou evidência verificável de que não houve alterações.
5. Configuração completa, proveniência e avisos/licenças de cada componente incorporado.
6. Um manifesto com URLs de origem e SHA-256 de todos os arquivos no pacote de fontes.

O texto de `-buildconf`, o repositório de código do FFmpeg atual e o asset binário da Gyan não substituem esses materiais. Não foi inventado nem produzido código-fonte, patch ou receita de build nesta etapa.

## Outros avisos/fontes pendentes

- **Deno 2.7.1:** a licença MIT do projeto está incluída. A árvore upstream examinada não forneceu um arquivo consolidado de licenças de dependências correspondente ao executável; é necessária a lista exata de crates/dependências da build e seus avisos/licenças.
- **yt-dlp 2026.08.19:** a licença Unlicense está incluída e o hash do executável coincide com o asset oficial. O executável inclui Python 3.10.11 e componentes de empacotamento/dependências; os avisos aplicáveis a esse bundle ainda precisam ser identificados.
- **FFmpeg:** os textos de licença do FFmpeg (GPLv2, GPLv3, LGPLv2.1 e LGPLv3) estão incluídos, mas isso não substitui os avisos/licenças e fontes das bibliotecas externas.

## Asset de fontes previsto e validação do build

Depois de obter e revisar todos os materiais, prepará-los como asset separado da mesma GitHub Release:

`GetIt-Third-Party-Sources-v0.1.2.zip`

O processo de build espera que o arquivo e estes dois acompanhamentos sejam preparados em `bin/ThirdPartySources/`:

- `GetIt-Third-Party-Sources-v0.1.2.zip`
- `GetIt-Third-Party-Sources-v0.1.2.zip.sha256` — somente o SHA-256 hexadecimal de 64 caracteres.
- `REDISTRIBUTION-REVIEW.txt` — linhas `version=0.1.2`, `source-bundle-sha256=<hash>` e `reviewed-by=<responsável>`.

O script verifica presença, hash e registro da revisão antes de publicar ou compilar o instalador. A presença e o hash não verificam juridicamente o conteúdo do arquivo; a revisão humana deve confirmar que o pacote é completo e que o asset será publicado junto do instalador em `https://github.com/lucasjordaoreal/getit/releases/tag/0.1.2`.

Até a conferência independente desses materiais, não marcar a release como pronta, não gerar um instalador distribuível e não publicar binários. A avaliação não constitui aconselhamento jurídico.
