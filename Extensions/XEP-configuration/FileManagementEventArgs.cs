using System;
using System.Collections.Generic;

namespace Sharp.Xmpp.Extensions
{
    public class FileManagementEventArgs : EventArgs
    {
        /// <summary>
        /// The file id 
        /// </summary>
        public List<String> FilesId { get; private set; }

        /// <summary>
        /// Action done on this file
        /// </summary>
        public String Action { get; private set; }

        public FileManagementEventArgs(List<String> filesId, String action)
        {
            FilesId = filesId;
            Action = action;
        }
    }
}
