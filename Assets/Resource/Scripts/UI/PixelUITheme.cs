using TMPro;
using UnityEngine;

namespace Resource.Scripts
{
    public sealed class PixelUITheme : ScriptableObject
    {
        public Sprite buttonNormal, buttonSelected, buttonDisabled, panel;
        public Sprite arrowLeft, arrowRight, arrowLeftDisabled, arrowRightDisabled;
        public Sprite checkOff, checkOn, dividerDot, barFrame, barFill, logo;
        public Sprite levelCardNormal, levelCardSelected, levelCardLocked, levelThumbnail;
        public Sprite levelLock, levelCheck, levelTitle;
        public TMP_FontAsset regularFont, boldFont;
        public TMP_FontAsset titleFont;
    }
}
