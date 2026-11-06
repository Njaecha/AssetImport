using System;
using UnityEngine;

namespace AssetImport
{
    internal partial class AssetUI
    {
        private GUISkin _chineseSkin;
        private Font _chineseFont;
        private bool _fontChecked;

        private GUISkin InterfaceSkin()
        {
            var source = KKAPI.Utilities.IMGUIUtils.SolidBackgroundGuiSkin;
            if (L.Language != InterfaceLanguage.ChineseSimplified) return source;
            if (!_fontChecked)
            {
                _fontChecked = true;
                try
                {
                    _chineseFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial Unicode MS" }, 14);
                    if (_chineseFont)
                    {
                        _chineseSkin = Instantiate(source);
                        _chineseSkin.font = _chineseFont;
                        foreach (GUIStyle style in _chineseSkin) style.font = _chineseFont;
                    }
                }
                catch (Exception error) { AssetImport.Logger.LogWarning("Could not load Chinese interface font: " + error.Message); }
            }
            return _chineseSkin ? _chineseSkin : source;
        }

        private void OnDestroy()
        {
            if (_chineseSkin) Destroy(_chineseSkin);
            if (_chineseFont) Destroy(_chineseFont);
        }
    }
}
