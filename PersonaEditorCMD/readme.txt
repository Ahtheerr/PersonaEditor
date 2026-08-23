PersonaEditorCMD user guide
===========================

PersonaEditorCMD is the command-line version of PersonaEditor. It can export,
import, and rebuild supported containers, images, font tables, and text files.

Portuguese guide: readme.pt-BR.txt


Basic syntax
------------

PersonaEditorCMD.exe "<file-or-folder>" -command [value] [/option [value]] ...

Examples:

PersonaEditorCMD.exe "event.bf" -exptext "event.txt" /sub /rmvspl
PersonaEditorCMD.exe "event.bf" -imptext "event.txt" /sub -save /ovrw
PersonaEditorCMD.exe "GameFiles" -exptext "AllText.txt" /sub /rmvspl
PersonaEditorCMD.exe "font.fnt" -exptable -impimage "font.png" -save

Options belong to the command immediately before them. For example, use
`-save /ovrw`, not `/ovrw` on an earlier command, when you want to overwrite the
saved file.


Input files and folders
-----------------------

The first argument can be a single file or a folder.

Single file:
  Commands run on that file. Use `/sub` when you want container subfiles to be
  processed recursively.

Folder:
  Commands run on every supported file found in the folder and its subfolders.
  Folder text export can write everything into one TXT:

  PersonaEditorCMD.exe "GameFiles" -exptext "Output.txt" /sub

  Duplicate text rows are collapsed at the end. If two rows only differ by file
  name, the file names are joined with `|`, for example:

  file_a.bmd|file_b.bmd    0    1    Old text    New text

  Text import understands this `|` file-name list and imports the row into each
  matching file.


Common commands
---------------

-exptext [TXT]
  Export text from PTP, BMD/MSG, ATF, MBM/MSG2/TBB1, Persona 5 Tactica (.dat/.bytes), and supported
  string-list files. If TXT is
  omitted, a TXT is created next to the source file.

-imptext [TXT]
  Import text into PTP, BMD/MSG, ATF, MBM/MSG2/TBB1, Persona 5 Tactica (.dat/.bytes), and supported
  string-list files. Usually
  followed by `-save`.

-expptp
  Export PTP files from BMD/MSG files. Use `/sub` for BMD files inside
  containers.

-impptp
  Import matching PTP files back into BMD/MSG files. Usually followed by
  `-save`.

-expimage
  Export supported images as PNG. Use `/sub` for images inside containers.

-impimage [PNG]
  Import a PNG into a supported image file. If PNG is omitted, the tool looks for
  a PNG with the same base name as the opened file.

-exptable
  Export a supported font/table file to XML.

-imptable [XML]
  Import a supported font/table XML. If XML is omitted, the tool looks for an XML
  with the same base name as the opened file.

-expall
  Export immediate subfiles from a container.

-impall
  Import matching immediate subfiles back into a container. Usually followed by
  `-save`.

-save [OUTPUT]
  Write the modified file. If OUTPUT is omitted, the default output name is
  `Name(NEW).ext`. Add `/ovrw` to overwrite the original file.


Useful options
--------------

/sub
  Process subfiles recursively when the opened file is a container.

/map "PATTERN"
  Tell `-imptext` how TSV columns should be read. Default:

  "%FN %MSGIND %STRIND %I %I %NEWSTR"

  Available fields:
  %I       ignore this column
  %FN      file name
  %MSGIND  message index
  %MSGNM   message name
  %STRIND  string index
  %OLDSTR  old/source text
  %NEWSTR  new/imported text
  %OLDNM   old/source speaker name
  %NEWNM   new/imported speaker name

/rmvspl
  On text export, replace line breaks inside strings with spaces.

/agr
  On folder text export, use aggressive line deduplication.
  Default deduplication only merges whole identical files.

/auto WIDTH
  On PTP text import, automatically wrap text using the selected font width.
  Example: `/auto 580`.

/co2n
  On PTP export, copy old/source text into the new/translated text column.

/lbl
  On PTP text import, import line by line instead of using message/string
  indexes.

/enc ENCODING
  Text file encoding. Default is UTF-8. Supported explicit values:
  UTF-7, UTF-16, UTF-32.

/size SIZE
  On FNT image import, resize the font texture before importing the PNG.

/bmd
  When saving a PTP file, save it as BMD.

/ovrw
  On `-save`, overwrite the original file instead of creating `Name(NEW).ext`.


Text import examples
--------------------

Export text from one container:

PersonaEditorCMD.exe "E100_000.BF" -exptext "E100_000.txt" /sub /rmvspl

Import edited text and overwrite the original:

PersonaEditorCMD.exe "E100_000.BF" -imptext "E100_000.txt" /sub -save /ovrw

Export every supported text file in a folder into one TXT:

PersonaEditorCMD.exe "GameFiles" -exptext "AllText.txt" /sub /rmvspl

Import that combined TXT back into every matching file:

PersonaEditorCMD.exe "GameFiles" -imptext "AllText.txt" /sub -save /ovrw

Import with an explicit TSV map:

PersonaEditorCMD.exe "event.ptp" -imptext "event.txt" /map "%FN %MSGIND %STRIND %I %I %NEWSTR" -save /ovrw


Supported formats
-----------------

The tool opens many formats through PersonaEditorLib, including:

Containers:
  BIN, FBIN, UASSET BMD, PAC, SPR, SPR3, SPR6, BF, PM1, BVP, TBL, TPC, LB

Images and textures:
  TMX, DDS, CTPK, G1T, STEX, CMP/DMPBM/STEX, HIP

Text and fonts:
  BMD/MSG, UASSET-wrapped Reload BMD (automatic UTF-8), MBM/MSG2/TBB1,
  Persona 5 Tactica (.dat/.bytes), PTP, ATF, FNT, FNT0, ABC sidecars for HIP fonts

Unknown or raw files may appear as DAT/HEX when exported from containers.


Font settings
-------------

PersonaEditorCMD creates `PersonaEditor.xml` next to the executable. Edit
`OldFont` and `NewFont` there when PTP import/export needs a specific font for
encoding or automatic line wrapping. Use the font name without the extension.
