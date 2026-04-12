using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Text;
using System.Linq;

namespace Lua{
    public static class UI{
        private static Sprite mainSprite;
        private static Image MAIN;
        public static TextMeshProUGUI TextConsol { get; private set; }
        private static string waitOfTheWorld = "";
        public static bool InitConsol(Canvas _canvas){
            if(_canvas == null) return false;
            var canvas = _canvas;
            if(mainSprite == null){
                Texture2D texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, new Color32(28, 28, 28, 170));
                texture.Apply();
                mainSprite = Sprite.Create(texture, new Rect(0,0,1,1), new Vector2(0.5f, 0.5f));
            }
            if(mainSprite == null) return false;

            MAIN = new GameObject("LUA_MAIN_UI_CONSOL").AddComponent<Image>();
            MAIN.transform.SetParent(canvas.transform, false);
            MAIN.sprite = mainSprite;
            RectTransform MaintRect = MAIN.rectTransform;
            MaintRect.anchorMin = Vector2.zero;
            MaintRect.anchorMax = Vector2.one;
            MaintRect.offsetMin = Vector2.zero;
            MaintRect.offsetMax = Vector2.zero;

            TextConsol = new GameObject("LUA_TEXT_UI_CONSOL").AddComponent<TextMeshProUGUI>();
            TextConsol.transform.SetParent(MAIN.transform, false);
            RectTransform textRect = TextConsol.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.offsetMin = new Vector2(10, 10);
            textRect.offsetMax = new Vector2(-10, -10);

            TextConsol.richText = true;
            TextConsol.alignment = TextAlignmentOptions.TopLeft;
            TextConsol.enableWordWrapping = false;
            TextConsol.overflowMode = TextOverflowModes.Overflow;
            TextConsol.gameObject.AddComponent<MouseScroll>();
            var foundFont = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                .FirstOrDefault(f => f.name == "VCR_OSD_MONO");
            if (foundFont == null) return false;
            TextConsol.font = foundFont;
            TextConsol.fontSize = 32f;
            DebugPrint("<color=#00ffd0>[LUA UI]</color> <i>Hello world!</i>");
            if(!String.IsNullOrEmpty(waitOfTheWorld))DebugPrint(waitOfTheWorld);
            waitOfTheWorld = "";

            MAIN.gameObject.SetActive(false);
            Debug.Log("[LUA UI] Ready to work");
            return true;
        }
        public static void DebugPrint(params object[] objs) {
            if (TextConsol == null){
                string l = string.Join(", ", objs.Select(o => o?.ToString() ?? "nil"));
                waitOfTheWorld += $"{l}\n";
            };
            string line = string.Join(", ", objs.Select(o => o?.ToString() ?? "nil"));
            TextConsol.text += $"{line}\n";
        }
        public static void Consol(){
            if(MAIN == null || TextConsol == null) return;
            bool _a = MAIN.gameObject.activeSelf;
            MAIN.gameObject.SetActive(!_a);
        }
        public static void Kill(){
            if(MAIN != null){
                MonoBehaviour.Destroy(MAIN.gameObject);
                MAIN = null;
                TextConsol = null;
            }
        }
        private class MouseScroll : MonoBehaviour
        {
            private RectTransform content;
            private float scrollSpeed = 35f;
            private float minY = 0f;
            private float maxY = 0f;
            void Awake() => content = gameObject.GetComponent<RectTransform>();
            void Update()
            {
                float mouseDelta = Input.mouseScrollDelta.y;
                if (Mathf.Abs(mouseDelta) < 0.1f) return;
                TextConsol.ForceMeshUpdate();
                float textHeight = TextConsol.renderedHeight;
                float viewportHeight = Screen.height;

                float maxY = Mathf.Max(0, textHeight - viewportHeight + 100); 

                Vector2 pos = content.anchoredPosition;
                pos.y -= mouseDelta * scrollSpeed;
                
                pos.y = Mathf.Clamp(pos.y, 0, maxY);
                content.anchoredPosition = pos;
            }
        }
    }
}