// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;

namespace Cathei.BakingSheet.Raw
{
    internal readonly struct RawSheetError
    {
        public string Reason { get; }
        public Exception Exception { get; }

        public RawSheetError(string reason, Exception exception = null)
        {
            Reason = reason;
            Exception = exception;
        }
    }
}
