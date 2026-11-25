using System;
using System.Collections.Generic;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// أساس مشترك للمحركات اللي حالياً ندعم كشفها فقط (نعرف نوع المحرك بثقة)
    /// لكن لسا ما بنينا منطق الاستخراج/التركيب الخاص فيها. هذا يخلي البرنامج
    /// يقول للمستخدم بصراحة "عرفنا المحرك، لكن الدعم الكامل قيد التطوير"
    /// بدل ما يفشل بصمت أو يعطي نتيجة خاطئة.
    /// </summary>
    public abstract class UnsupportedYetAdapterBase : IEngineAdapter
    {
        public abstract EngineType Engine { get; }
        public abstract string DisplayName { get; }

        public int DetectConfidence(string gameFolderPath)
        {
            foreach (var r in EngineDetector.DetectAll(gameFolderPath))
                if (r.Engine == Engine) return r.Confidence;
            return 0;
        }

        public IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath) =>
            throw new NotSupportedException(
                $"دعم استخراج النصوص لمحرك {DisplayName} قيد التطوير حالياً. " +
                "تم التعرف على المحرك بنجاح، لكن لا يوجد بعد Adapter كامل له.");

        public bool ApplyTranslations(string gameFolderPath, IReadOnlyList<TextEntry> entries) =>
            throw new NotSupportedException($"تركيب الترجمة لمحرك {DisplayName} قيد التطوير حالياً.");

        public bool RemoveArabization(string gameFolderPath) =>
            throw new NotSupportedException($"إزالة التعريب لمحرك {DisplayName} قيد التطوير حالياً.");
    }

    public class GameMakerEngineAdapter : UnsupportedYetAdapterBase
    {
        public override EngineType Engine => EngineType.GameMaker;
        public override string DisplayName => "GameMaker";
        // ملاحظة تطوير: النصوص مدمجة داخل data.win (صيغة ثنائية بالكامل، ما
        // فيها ملفات حرة إطلاقاً عادةً)، يحتاج أداة مثل UndertaleModTool
        // (مفتوحة المصدر) كمرجع لتحليل صيغة الملف. آخر محرك ناقص من الستة.
    }
}
