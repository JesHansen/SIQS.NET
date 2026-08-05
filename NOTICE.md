# Notices and third-party content

The source code, documentation, and build tooling in this repository are released under the
[MIT License](LICENSE), Copyright (c) 2026 Jes Bak Hansen.

**The MIT License does not apply to the image files listed below.** They are third-party works
included under their own terms, and the copyright in each remains with its owner. If you fork,
redistribute, or deploy this repository, these files carry obligations the MIT License does not
grant you and does not waive.

Per-file provenance, authorship, and display credits are recorded in
[`SIQS.UI/wwwroot/img/history/CREDITS.md`](SIQS.UI/wwwroot/img/history/CREDITS.md).

## Images in `SIQS.UI/wwwroot/img/history/`

| File | Author / rights holder | Terms |
| --- | --- | --- |
| `pomerance.jpg` | Eli Burakian; photo © Trustees of Dartmouth College | **Not free-licensed.** An institutional press photograph, included with attribution. No permission to redistribute has been obtained from the rights holder. |
| `lenstra.jpg` | Rama (Wikimedia Commons) | CC BY-SA 2.0 FR. Copyleft: attribution and a license link are required, modifications must be indicated (this copy is resized), and derivative works must be shared alike. |
| `fermat.jpg` | François de Poilly (17th-century engraving) | Public domain (copyright expired). |
| `kraitchik-icm-1932.jpg` | Johannes Meiner (1867–1941); ETH-Bibliothek Zürich | Public domain (PD-old). |
| `montgomery.jpg` | Dcoetzee (Wikimedia Commons) | CC0 1.0 public-domain dedication. |

### If you redistribute this repository

- **`pomerance.jpg`** is the one to look at first. It is a copyrighted press photograph used with
  attribution and without a license grant. Including it in a public distribution is a judgement the
  maintainer has made for this repository; it is not a judgement that transfers to yours. Removing
  the file (and the corresponding entry in `History.razor`) is the safe option, and the History page
  renders without it.
- **`lenstra.jpg`** is copyleft, not permissive. Keeping it means honouring CC BY-SA 2.0 FR:
  attribute Rama, link the license, note that the file was resized, and license any derivative of
  the image under the same terms. This is compatible with distributing the *code* under MIT, because
  the image is not a derivative work of the code — but the image itself is not MIT.
- The remaining three images are public domain or CC0 and carry no redistribution conditions. The
  display credits are kept because attribution is good manners, not because it is required.

## Software this project learned from

SIQS.NET contains no code copied from other factoring implementations. It nonetheless owes a great
deal to [msieve](https://github.com/radii/msieve) (Jason Papadopoulos) and
[YAFU](https://github.com/bbuhrow/yafu) (Ben Buhrow and contributors), whose source, documentation,
and recorded engineering decisions informed the design. Where a parameter choice or an algorithmic
approach follows theirs, the comment beside it says so.
