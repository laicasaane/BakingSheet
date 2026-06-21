using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cathei.BakingSheet.Editor
{
    public static class BuildTestTools
    {
        [MenuItem("BakingSheet/Build Test/Standard")]
        public static void BuildTest()
        {
            BuildTestInternal();
        }

        [MenuItem("BakingSheet/Build Test/Runtime Google")]
        public static void BuildTestRuntimeGoogle()
        {
            BuildTestInternal("BAKINGSHEET_RUNTIME_GOOGLECONVERTER");
        }

        [MenuItem("BakingSheet/Build Test/Runtime CSV")]
        public static void BuildTestRuntimeCsv()
        {
            BuildTestInternal("BAKINGSHEET_RUNTIME_CSVCONVERTER");
        }

        public static void BuildTestInternal(params string[] definingSymbol)
        {
#if UNITY_6000_3_OR_NEWER
            var currentGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(currentGroup);
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, definingSymbol);
#else
            var currentGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            PlayerSettings.SetScriptingDefineSymbolsForGroup(currentGroup, definingSymbol);
#endif

            BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup,
                target = EditorUserBuildSettings.activeBuildTarget,
                // extraScriptingDefines = definingSymbol,
                locationPathName = "../Build/BuildTest"
            });
        }
    }
}
