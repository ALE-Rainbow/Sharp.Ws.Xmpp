using System;

namespace Sharp.Xmpp.Extensions
{
    public class FileManagementEventArgs : EventArgs
    {
        /// <summary>
        /// The file id 
        /// </summary>
        public String FileId { get; private set; }

        /// <summary>
        /// Action done on this file
        /// </summary>
        public String Action { get; private set; }

        public FileManagementEventArgs(String fileId, String action)
        {
            FileId = fileId;
            Action = action;
        }
    }
}
