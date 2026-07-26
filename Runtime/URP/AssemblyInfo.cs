#if DAWNTOD_URP_AVAILABLE
using System.Runtime.CompilerServices;
using UnityEngine.Scripting;

[assembly: AlwaysLinkAssembly]
[assembly: InternalsVisibleTo("DawnTOD.Editor.URP")]
[assembly: InternalsVisibleTo("DawnTOD.Tests")]
[assembly: InternalsVisibleTo("DawnTOD.Editor.Tests")]
#endif
