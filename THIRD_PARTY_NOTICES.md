# Third-party notices

Altrobe includes code ported from, and depends on, the open-source projects below.

## wow.export

Parts of `tools/probe/Convert/` (M2, skin, skeleton, animation and BLP parsing), `tools/looks/looks.ts`, and `tools/viewer-test/` (`index.html`, `compositor.js`, `dress.js`, `m2-material.js`) are ported from [wow.export](https://github.com/Kruithne/wow.export). Each of those files says so in its header.

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

## Dependencies

These are downloaded at build or run time and are not copied into this repository:

- [TACTSharp](https://github.com/wowdev/TACTSharp) (NuGet), MIT License. Reads Blizzard's file storage.
- [DBCD](https://github.com/wowdev/DBCD) (NuGet), MIT License. Reads DB2 tables.
- [WoWDBDefs](https://github.com/wowdev/WoWDBDefs) table definitions, downloaded at run time. The definitions are CC BY-SA 4.0 and the code is BSD-3-Clause.
- [three.js](https://github.com/mrdoob/three.js), loaded from the jsDelivr CDN, MIT License.
