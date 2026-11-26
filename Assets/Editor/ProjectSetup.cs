using UnityEditor;
using UnityEngine;

namespace Jianghu.EditorTools
{
    /// <summary>
    /// 開啟專案時檢查輸入設定。本遊戲使用傳統 Input Manager；
    /// 若專案被設成「只用新 Input System」，滑鼠鍵盤 API 會丟例外，這裡自動改回來。
    /// </summary>
    [InitializeOnLoad]
    static class ProjectSetup
    {
        static ProjectSetup()
        {
            EditorApplication.delayCall += Check;
        }

        static void Check()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null || prop.intValue != 1) return; // 0 傳統、1 只用新系統、2 兩者並用

            prop.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("江湖骰",
                "已把輸入設定改為傳統 Input Manager。\n請關閉並重新開啟 Unity，再按 Play。", "好");
        }
    }
}
