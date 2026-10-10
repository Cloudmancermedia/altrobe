# Third-party notices

Altrobe includes code ported from, and depends on, the open-source projects below.

## wow.export

Parts of `tools/probe/Convert/` and its port in `importer/src/Altrobe.Core/Convert/` (M2, skin, skeleton, animation and BLP parsing, and the animation name table in `AnimationNames.cs`, which wow.export took from [wow.tools.local](https://github.com/Marlamin/wow.tools.local)), `tools/looks/looks.ts`, and `tools/viewer-test/` (`index.html`, `compositor.js`, `dress.js`, `m2-material.js`) are ported from [wow.export](https://github.com/Kruithne/wow.export). Each of those files says so in its header.

```
MIT License

Copyright (c) Kruithne <kruithne@gmail.com>
Copyright (c) Marlamin <marlamin@marlamin.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## WoWDBDefs table definitions

`importer/src/Altrobe.Core/Definitions/` holds unmodified copies of the `.dbd` files in that folder,
from [WoWDBDefs](https://github.com/wowdev/WoWDBDefs) (commit `e989e99e6f5f97c57b2e138d4d28b16066b4ee9c`),
plus its `manifest.json` trimmed to the same tables.

The definitions are licensed under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)
by the WoWDBDefs contributors. They describe file layouts and contain no Blizzard game data.

## cmangos classic-db item names

The names, quality and levels of Classic items the Forever game files leave unnamed come from
[cmangos classic-db](https://github.com/cmangos/classic-db), a community database of the 1.12.1 client,
at commit `28ef6259c782928b08a8dc9cacf6bc02e64f2b29`. It is licensed under the
[GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html). The data is not in this
repository: the app and the bake download it at run time (`ClassicItemNames`), use it only where the game
files have no name, and credit it in the website bundle's `catalog.json`.

## Marcellus font

The web app's headings use [Marcellus](https://fonts.google.com/specimen/Marcellus) by Brian J.
Bonislawsky (Astigmatic), through the `@fontsource/marcellus` package, bundled with the app rather than
loaded from a font service. It is licensed under the
[SIL Open Font License 1.1](https://openfontlicense.org); the license text ships in the package.

## Dependencies

These are downloaded at build or run time and are not copied into this repository:

- [TACTSharp](https://github.com/wowdev/TACTSharp) (NuGet), MIT License. Reads Blizzard's file storage.
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (NuGet: ModelContextProtocol.AspNetCore and its ModelContextProtocol and ModelContextProtocol.Core packages), Apache License 2.0. Serves the MCP endpoint. It depends on Microsoft.Extensions.AI.Abstractions, MIT License.
- [DBCD](https://github.com/wowdev/DBCD) (NuGet), MIT License. Reads DB2 tables.
- [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp) (NuGet: Anthropic), MIT License. Calls Claude for the prompt box with the player's own key.
- [OpenAI .NET library](https://github.com/openai/openai-dotnet) (NuGet: OpenAI, with System.ClientModel), MIT License. Calls OpenAI-compatible servers for the prompt box.
- [Microsoft.Extensions.AI](https://github.com/dotnet/extensions) (NuGet: Microsoft.Extensions.AI and Microsoft.Extensions.AI.OpenAI), MIT License. Runs the prompt box's tool calls.
- [WoWDBDefs](https://github.com/wowdev/WoWDBDefs) table definitions for tables or builds the vendored copies above do not cover, downloaded at run time into Altrobe's cache folder. The definitions are CC BY-SA 4.0 and the code is BSD-3-Clause.
- [three.js](https://github.com/mrdoob/three.js) (npm), MIT License. The `tools/viewer-test/` prototype loads it from the jsDelivr CDN.

## In the downloadable packages

The release zips (built by `npm run package`) include compiled copies of the software below, in
the `Altrobe` executable or its `wwwroot` folder. Each is under the MIT License, whose text is
reproduced under wow.export above, with the copyright line shown here.

- .NET runtime and ASP.NET Core: Copyright (c) .NET Foundation and Contributors. <https://github.com/dotnet/runtime>, <https://github.com/dotnet/aspnetcore>
- TACTSharp: Copyright (c) 2024 Martin Benjamins. <https://github.com/wowdev/TACTSharp>
- DBCD and DBCD.IO: Copyright (c) 2020 wowdev. <https://github.com/wowdev/DBCD>
- Microsoft.Extensions.AI, Microsoft.Extensions.AI.Abstractions and Microsoft.Extensions.AI.OpenAI: Copyright (c) Microsoft Corporation. <https://github.com/dotnet/extensions>
- Anthropic C# SDK: Copyright 2026 Anthropic. <https://github.com/anthropics/anthropic-sdk-csharp>
- OpenAI .NET library: Copyright (c) 2026 OpenAI. <https://github.com/openai/openai-dotnet>
- System.ClientModel: Copyright (c) Microsoft Corporation. <https://github.com/Azure/azure-sdk-for-net>
- React, React DOM and scheduler: Copyright (c) Meta Platforms, Inc. and affiliates. <https://github.com/facebook/react>
- three.js: Copyright (c) 2010-2026 three.js authors. <https://github.com/mrdoob/three.js>

The WoWDBDefs definitions listed above are built into the executable, under CC BY-SA 4.0.

The Claude Desktop extension (`Altrobe-<version>.mcpb`, built by `npm run package:desktop`) bundles
the software below into `server/index.js`, each under the MIT License:

- MCP TypeScript SDK (`@modelcontextprotocol/sdk`): Copyright (c) 2024 Anthropic, PBC. <https://github.com/modelcontextprotocol/typescript-sdk>
- content-type: Copyright (c) 2015 Douglas Christopher Wilson. <https://github.com/jshttp/content-type>
- eventsource-parser: Copyright (c) 2026 Espen Hovlandsdal. <https://github.com/rexxars/eventsource-parser>
- pkce-challenge: Copyright (c) 2019 (its licence names no holder). <https://github.com/crouchcd/pkce-challenge>
- zod: Copyright (c) 2025 Colin McDonnell. <https://github.com/colinhacks/zod>

The executable also includes the MCP C# SDK (ModelContextProtocol, ModelContextProtocol.Core and
ModelContextProtocol.AspNetCore), Copyright (c) Model Context Protocol a Series of LF Projects,
LLC, <https://github.com/modelcontextprotocol/csharp-sdk>, under the Apache License 2.0:

```
Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS
```
