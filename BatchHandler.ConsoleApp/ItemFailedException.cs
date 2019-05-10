using System;
using System.Runtime.Serialization;

namespace BatchHandler.ConsoleApp
{
    /// <summary>
    /// Represents the exception that is thrown by the "API" every now an then.
    /// </summary>
    [Serializable]
    public class ItemFailedException : Exception
    {
        public ItemFailedException()
        {
        }

        public ItemFailedException(string message) : base(message)
        {
        }

        public ItemFailedException(string message, Exception inner) : base(message, inner)
        {
        }

        protected ItemFailedException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
}