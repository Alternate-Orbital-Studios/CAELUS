Plugin source code of this package (MIT licence, see LICENSE-CODE.txt).

Each folder holds the C# files one plugin DLL is built from. Build with the C# compiler (csc) against the game's
KSP_x64_Data/Managed assemblies (Assembly-CSharp.dll, UnityEngine*.dll) and, where noted, 000_Harmony/0Harmony.dll.

CaelusCameraFloor/  ->  CaelusCameraFloor.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
CaelusGroundFit/  ->  CaelusGroundFit.dll   (csc (C# 7.3), references: KSP_x64_Data/Managed)
CaelusNearCloudShadow/  ->  CaelusNearCloudShadow.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
CaelusSiteLights/  ->  CaelusSiteLights.dll   (csc (C# 7.3), .NET Framework 4.x target, references: KSP_x64_Data/Managed (Assembly-CSharp, UnityEngine.*), 000_Harmony/0Harmony.dll)
StatioRequirements/  ->  StatioRequirements.dll   (csc (C# 7.3), the same Requirements.cs built once per package (see the package's dependency list in the file))
