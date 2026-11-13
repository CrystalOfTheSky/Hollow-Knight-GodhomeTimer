using System;
using Modding;
using UnityEngine;

namespace GodhomeBossTimerMod
{
    /// <summary>
    /// Built-in Hollow Knight fonts that are safe to use without shipping font
    /// assets with the mod.
    /// </summary>
    internal static class TimerFontCatalog
    {
        public static readonly string[] Labels =
        {
            "Trajan Bold",
            "Trajan Regular",
            "Perpetua",
            "Noto Serif CJK"
        };

        public static readonly string[] ResourceNames =
        {
            "TrajanPro-Bold",
            "TrajanPro-Regular",
            "Perpetua",
            "NotoSerifCJKsc-Regular"
        };

        public static string Normalize(string configuredName)
        {
            if (!string.IsNullOrEmpty(configuredName) &&
                Array.IndexOf(ResourceNames, configuredName) >= 0)
            {
                return configuredName;
            }

            return ResourceNames[0];
        }

        public static int GetIndex(string configuredName)
        {
            int index = Array.IndexOf(ResourceNames, Normalize(configuredName));
            return index < 0 ? 0 : index;
        }

        public static Font Resolve(string configuredName)
        {
            string resourceName = Normalize(configuredName);
            Font font = CanvasUtil.GetFont(resourceName);
            if (font != null)
            {
                return font;
            }

            if (resourceName == "TrajanPro-Regular")
            {
                font = CanvasUtil.TrajanNormal;
            }
            else if (resourceName == "TrajanPro-Bold")
            {
                font = CanvasUtil.TrajanBold;
            }

            return font != null ? font : CanvasUtil.TrajanBold;
        }
    }
}
