﻿using System.Threading.Tasks;
using AcManager.Pages.Dialogs;
using AcManager.Tools.Objects;

namespace AcManager.Tools {
    public static class CarSoundReplacer {
        public static double RpmLimiterThreshold = 500;

        public static async Task<bool> Replace(CarObject car) {
            var maxRpm = car.AcdData?.GetIniFile("engine.ini")["ENGINE_DATA"].GetFloat("LIMITER", 0) ?? car.GetRpmMaxValue();
            var donor = SelectCarDialog.Show(double.IsNaN(maxRpm) || maxRpm < 1000 ? null : $"maxrpm≥{maxRpm - RpmLimiterThreshold:F0}");
            if (donor == null) return false;

            await car.ReplaceSound(donor);
            return true;
        }
    }
}