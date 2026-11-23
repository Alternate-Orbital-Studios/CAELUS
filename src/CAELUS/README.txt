Plugin source code of this package (MIT licence, see LICENSE-CODE.txt).

Each folder holds the C# files one plugin DLL is built from. Build with the C# compiler (csc) against the game's
KSP_x64_Data/Managed assemblies (Assembly-CSharp.dll, UnityEngine*.dll) and, where noted, 000_Harmony/0Harmony.dll.

CaelusEntryDrag/  ->  CaelusEntryDrag.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
CaelusEntryGlow/  ->  CaelusEntryGlow.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
CaelusLegLanded/  ->  CaelusLegLanded.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
CaelusNaNGuard/  ->  CaelusNaNGuard.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
CaelusRequirements/  ->  CaelusRequirements.dll   (csc (C# 7.3), the same Requirements.cs built once per package (see the package's dependency list in the file))
CaelusSkyAmbient/  ->  CaelusSkyAmbient.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
CaelusSmoke/  ->  CaelusSmoke.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
RomaAeterna.InteriorBounds/  ->  RomaAeterna.InteriorBounds.dll   (.NET Framework csc (C# 5), references: KSP_x64_Data/Managed)
RomaAeterna/  ->  RomaAeterna.dll   (.NET Framework csc (C# 5), references: KSP_x64_Data/Managed; FreeIva is reached by reflection only (not linked))
