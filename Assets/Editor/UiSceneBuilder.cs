using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// UI一式をシーンのヒエラルキーに実体化するツール。
// 生成されたUIは MainGame が実行時に名前で見つけて再利用するため、
// ヒエラルキー上で位置・大きさ・色・文字を手動編集すると、そのままゲームに反映されます。
// 使い方: Unityエディタのメニュー「RenaiProto2 > UIをシーンに生成」を実行し、シーンを保存する。
public static class UiSceneBuilder
{
    private const string RoundedPngPath = "Assets/UI/rounded_panel.png";

    [MenuItem("RenaiProto2/UIをシーンに生成")]
    public static void BuildIntoScene()
    {
        var existing = GameObject.Find("Canvas");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("UIをシーンに生成",
                    "シーンには既に Canvas（生成済みのUI）があります。\n削除して作り直すと、手動で編集した内容は消えます。作り直しますか？",
                    "作り直す", "キャンセル"))
                return;
            Object.DestroyImmediate(existing);
            var es = GameObject.Find("EventSystem");
            if (es != null) Object.DestroyImmediate(es);
        }

        var mg = Object.FindObjectOfType<MainGame>();
        if (mg == null)
        {
            var go = new GameObject("MainGame");
            mg = go.AddComponent<MainGame>();
        }
        mg.BuildUiForEditor();
        mg.ClearDynamicChildrenForEditor();
        SaveRoundedSpriteAsAsset();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("UIをシーンに生成",
            "ヒエラルキーに Canvas 以下、UI一式を生成しました。\nシーンを保存（Ctrl+S）してください。\n各オブジェクトを選んで位置・大きさ・色・文字を変更でき、再生するとそのまま反映されます。",
            "OK");
    }

    // コード生成の角丸スプライトはメモリ上にしか無くシーンに保存できないため、
    // PNGアセットとして書き出して、使っている Image すべてに付け替える。
    private static void SaveRoundedSpriteAsAsset()
    {
        var canvas = GameObject.Find("Canvas");
        if (canvas == null) return;
        var images = canvas.GetComponentsInChildren<Image>(true);
        Texture2D tex = null;
        foreach (var img in images)
        {
            if (img.sprite != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(img.sprite)))
            {
                tex = img.sprite.texture;
                break;
            }
        }
        if (tex == null) return;

        Directory.CreateDirectory("Assets/UI");
        File.WriteAllBytes(RoundedPngPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(RoundedPngPath);
        var importer = AssetImporter.GetAtPath(RoundedPngPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteBorder = new Vector4(26, 26, 26, 26);
            importer.SaveAndReimport();
        }
        var saved = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedPngPath);
        if (saved == null) return;
        foreach (var img in images)
        {
            if (img.sprite != null && img.sprite.texture == tex)
                img.sprite = saved;
        }
    }
}
