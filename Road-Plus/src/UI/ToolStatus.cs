// Aracın anlık durumu; bilgi paneli (TF2Hud) bunu okur.
using TF2YolAraci.Core;
using TF2YolAraci.Systems;
using UnityEngine;

namespace TF2YolAraci.UI
{
    public static class ToolStatus
    {
        public static bool active;
        public static string roadName = "";
        public static RoadClass roadClass;
        public static double[] allowedGrades = new double[0];
        public static double slopeLimitForClass;

        /// <summary>Panelin ekrandaki alanı (IMGUI koordinatları, piksel). Üzerine tıklanınca yol yapılmaz.</summary>
        public static Rect panelRect;

        /// <summary>Fare panelin üzerinde mi (IMGUI içinden, her olayda güncellenir).</summary>
        public static bool pointerOverPanel;

        /// <summary>Panelin kapat düğmesi basıldı; aktivatör bir sonraki karede TF2 modunu kapatır.</summary>
        public static bool closeRequested;

        public static HeightMode mode;
        public static SmoothingLevel smoothing;
        public static double grade;
        public static float heightOffset;
        public static bool tangentLock;
        public static bool hasStart;

        public static ToolMode tool;
        public static ConnectShape shape;
        public static EvenHeight connectHeight;
        public static EvenHeight slopeHeight;
        public static float spiralRadius;
        public static bool spiralFlip;
        public static int spiralTurns = -1;
        public static bool canUndo;

        // Fare konumuna göre hesaplanan önizleme
        public static bool hasPlan;
        public static double length;
        public static double maxSlope;
        public static double slopeLimit;
        public static bool canBuild;
        public static string message = "";

        public static void ClearPlan()
        {
            hasPlan = false;
            length = 0;
            maxSlope = 0;
            canBuild = false;
            message = "";
            spiralTurns = -1;
        }
    }
}
