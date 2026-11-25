using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hufra.Core.Models
{
    /// <summary>
    /// يمثّل نص واحد قابل للتعريب داخل ملف من ملفات اللعبة.
    /// </summary>
    public class TextEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>معرف فريد يُستخدم لإرجاع الترجمة لمكانها الصحيح وقت التركيب</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>مسار الملف الذي استُخرج منه النص (نسبي لمجلد اللعبة)</summary>
        public string SourceFile { get; set; }

        /// <summary>مسار/مفتاح داخل الملف (مثلاً JSON path أو رقم سطر) يحدد مكان النص بدقة</summary>
        public string LocationKey { get; set; }

        /// <summary>النص الأصلي كما استُخرج</summary>
        public string OriginalText { get; set; }

        private string _translatedText;
        /// <summary>النص بعد الترجمة (فارغ لين تتم الترجمة)</summary>
        public string TranslatedText
        {
            get => _translatedText;
            set { _translatedText = value; Raise(); }
        }

        /// <summary>سياق اختياري يساعد المترجم (مثلاً: "حوار NPC: التاجر")</summary>
        public string Context { get; set; }

        /// <summary>هل هذا النص يحتوي متغيرات برمجية يجب الحفاظ عليها (مثل {0}, %s, [item])</summary>
        public bool HasPlaceholders { get; set; }

        private string _note;
        /// <summary>ملاحظة مراجعة تلقائية (مثلاً: المتغيرات تغيّرت). فارغة = الترجمة سليمة</summary>
        public string Note
        {
            get => _note;
            set { _note = value; Raise(); }
        }

        private bool _isApproved;
        /// <summary>هل تمت مراجعة المستخدم لهذه الترجمة والموافقة عليها</summary>
        public bool IsApproved
        {
            get => _isApproved;
            set { _isApproved = value; Raise(); }
        }
    }
}
