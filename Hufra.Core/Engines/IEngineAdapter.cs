using System.Collections.Generic;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// العقد الذي يربط "حُفرة" بأي محرك لعبة جديد. كل محرك (Unity, Godot, ...)
    /// له كلاس ينفّذ هذه الواجهة، وباقي البرنامج يتعامل مع المحركات كلها
    /// بنفس الطريقة بدون ما يعرف تفاصيلها الداخلية.
    ///
    /// إضافة محرك جديد لاحقاً = كلاس جديد ينفّذ هذه الواجهة + تسجيله في
    /// EngineDetector، بدون تعديل أي كود ثاني في البرنامج.
    /// </summary>
    public interface IEngineAdapter
    {
        EngineType Engine { get; }

        /// <summary>اسم يظهر للمستخدم، مثل "Unity" أو "RPG Maker MV/MZ"</summary>
        string DisplayName { get; }

        /// <summary>
        /// يفحص مجلد اللعبة ويرجّع درجة ثقة من 0 إلى 100 إن هذا فعلاً محرك هذا الـ Adapter.
        /// EngineDetector يجرّب كل الـ Adapters ويختار الأعلى ثقة.
        /// </summary>
        int DetectConfidence(string gameFolderPath);

        /// <summary>
        /// يفحص ملفات اللعبة ويستخرج كل النصوص القابلة للتعريب.
        /// لا يعدّل أي ملف في هذه الخطوة — قراءة فقط.
        /// </summary>
        IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath);

        /// <summary>
        /// يطبّق الترجمات المعتمدة على ملفات اللعبة الفعلية.
        /// يجب استدعاء CreateBackup قبل هذه الخطوة دائماً (يتكفّل بها BackupManager في الطبقة الأعلى، مو هنا).
        /// </summary>
        /// <returns>true لو التركيب نجح بالكامل</returns>
        bool ApplyTranslations(string gameFolderPath, IReadOnlyList<TextEntry> entries);

        /// <summary>يرجّع اللعبة لحالتها الأصلية (استعادة من نسخة احتياطية محفوظة مسبقاً)</summary>
        bool RemoveArabization(string gameFolderPath);
    }
}
