Guia do PersonaEditorCMD
========================

PersonaEditorCMD é a versão de linha de comando do PersonaEditor. Ele exporta,
importa e salva containers, imagens, tabelas de fonte e textos suportados.

Guia em inglês: readme.txt


Sintaxe básica
--------------

PersonaEditorCMD.exe "<arquivo-ou-pasta>" -comando [valor] [/opção [valor]] ...

Exemplos:

PersonaEditorCMD.exe "event.bf" -exptext "event.txt" /sub /rmvspl
PersonaEditorCMD.exe "event.bf" -imptext "event.txt" /sub -save /ovrw
PersonaEditorCMD.exe "GameFiles" -exptext "AllText.txt" /sub /rmvspl
PersonaEditorCMD.exe "font.fnt" -exptable -impimage "font.png" -save

As opções pertencem ao comando que vem imediatamente antes delas. Por exemplo,
use `-save /ovrw` para sobrescrever o arquivo salvo.


Arquivos e pastas
-----------------

O primeiro argumento pode ser um arquivo ou uma pasta.

Arquivo:
  Os comandos são executados nesse arquivo. Use `/sub` para processar
  subarquivos dentro de containers.

Pasta:
  Os comandos são executados em todos os arquivos suportados encontrados na
  pasta e nas subpastas. Exportação de texto em pasta pode salvar tudo em um
  único TXT:

  PersonaEditorCMD.exe "GameFiles" -exptext "Output.txt" /sub

  No final, linhas duplicadas são unificadas. Se apenas o nome do arquivo for
  diferente, os nomes são unidos com `|`:

  file_a.bmd|file_b.bmd    0    1    Texto antigo    Texto novo

  A importação entende essa lista com `|` e aplica a linha em cada arquivo
  correspondente.


Comandos principais
-------------------

-exptext [TXT]
  Exporta texto de PTP, BMD/MSG, ATF, MBM/MSG2, Persona 5 Tactica (.dat/.bytes) e listas de texto
  suportadas. Se o TXT for
  omitido, cria um TXT ao lado do arquivo de origem.

-imptext [TXT]
  Importa texto em PTP, BMD/MSG, ATF, MBM/MSG2, Persona 5 Tactica (.dat/.bytes) e listas de texto
  suportadas. Normalmente
  deve ser seguido por `-save`.

-expptp
  Exporta PTP de arquivos BMD/MSG. Use `/sub` para BMDs dentro de containers.

-impptp
  Importa PTPs correspondentes de volta para BMD/MSG. Normalmente deve ser
  seguido por `-save`.

-expimage
  Exporta imagens suportadas como PNG. Use `/sub` para imagens dentro de
  containers.

-impimage [PNG]
  Importa um PNG em uma imagem suportada. Se o PNG for omitido, procura um PNG
  com o mesmo nome base do arquivo aberto.

-exptable
  Exporta uma tabela/fonte suportada para XML.

-imptable [XML]
  Importa um XML de tabela/fonte. Se o XML for omitido, procura um XML com o
  mesmo nome base do arquivo aberto.

-expall
  Exporta os subarquivos imediatos de um container.

-impall
  Importa subarquivos correspondentes de volta para um container. Normalmente
  deve ser seguido por `-save`.

-save [SAIDA]
  Salva o arquivo modificado. Se SAIDA for omitida, o nome padrão será
  `Nome(NEW).ext`. Use `/ovrw` para sobrescrever o original.


Opções úteis
------------

/sub
  Processa subarquivos recursivamente quando o arquivo aberto é um container.

/map "PADRÃO"
  Diz ao `-imptext` como ler as colunas do TSV. Padrão:

  "%FN %MSGIND %STRIND %I %I %NEWSTR"

  Campos disponíveis:
  %I       ignora a coluna
  %FN      nome do arquivo
  %MSGIND  índice da mensagem
  %MSGNM   nome da mensagem
  %STRIND  índice da string
  %OLDSTR  texto antigo/original
  %NEWSTR  texto novo/importado
  %OLDNM   nome antigo/original
  %NEWNM   nome novo/importado

/rmvspl
  Na exportação de texto, troca quebras de linha internas por espaços.

/agr
  Na exportação de texto por pasta, usa deduplicação agressiva por linha.
  Por padrão, a deduplicação só junta arquivos inteiros idênticos.

/auto LARGURA
  Na importação de texto PTP, quebra linhas automaticamente usando a largura da
  fonte selecionada. Exemplo: `/auto 580`.

/co2n
  Na exportação de PTP, copia o texto antigo/original para a coluna de texto
  novo/traduzido.

/lbl
  Na importação de texto PTP, importa linha por linha em vez de usar índices de
  mensagem/string.

/enc ENCODING
  Encoding do arquivo de texto. O padrão é UTF-8. Valores explícitos:
  UTF-7, UTF-16, UTF-32.

/size TAMANHO
  Na importação de imagem FNT, redimensiona a textura da fonte antes de importar
  o PNG.

/bmd
  Ao salvar um PTP, salva como BMD.

/ovrw
  No `-save`, sobrescreve o arquivo original em vez de criar `Nome(NEW).ext`.


Exemplos de texto
-----------------

Exportar texto de um container:

PersonaEditorCMD.exe "E100_000.BF" -exptext "E100_000.txt" /sub /rmvspl

Importar texto editado e sobrescrever o original:

PersonaEditorCMD.exe "E100_000.BF" -imptext "E100_000.txt" /sub -save /ovrw

Exportar todo texto suportado de uma pasta para um único TXT:

PersonaEditorCMD.exe "GameFiles" -exptext "AllText.txt" /sub /rmvspl

Importar esse TXT combinado de volta:

PersonaEditorCMD.exe "GameFiles" -imptext "AllText.txt" /sub -save /ovrw

Importar usando um mapa TSV explícito:

PersonaEditorCMD.exe "event.ptp" -imptext "event.txt" /map "%FN %MSGIND %STRIND %I %I %NEWSTR" -save /ovrw


Formatos suportados
-------------------

A ferramenta abre vários formatos pelo PersonaEditorLib, incluindo:

Containers:
  BIN, PAC, SPR, SPR3, SPR6, BF, PM1, BVP, TBL, TPC, LB

Imagens e texturas:
  TMX, DDS, CTPK, G1T, STEX, CMP/DMPBM/STEX, HIP

Texto e fontes:
  BMD/MSG, MBM/MSG2, Persona 5 Tactica (.dat/.bytes), PTP, ATF, FNT, FNT0, sidecars ABC para fontes HIP

Arquivos desconhecidos ou crus podem aparecer como DAT/HEX quando exportados de
containers.


Configuração de fontes
----------------------

PersonaEditorCMD cria `PersonaEditor.xml` ao lado do executável. Edite `OldFont`
e `NewFont` quando importação/exportação PTP precisar de uma fonte específica
para encoding ou quebra automática de linhas. Use o nome da fonte sem extensão.
