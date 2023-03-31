using System;

namespace Sharp.Xmpp.Extensions
{
    public class VoiceMailManagementEventArgs : EventArgs
    {
        /// <summary>
        /// The id 
        /// </summary>
        public String Id { get; private set; }

        /// <summary>
        /// The file id 
        /// </summary>
        public String FileId { get; private set; }

        /// <summary>
        /// Action done on this file
        /// </summary>
        public String Action { get; private set; }

        /// <summary>
        /// Url to get the file
        /// </summary>
        public String Url { get; private set; }

        /// <summary>
        /// Mime type of the file
        /// </summary>
        public String MimeType { get; private set; }

        /// <summary>
        /// File name
        /// </summary>
        public String FileName { get; private set; }

        /// <summary>
        /// Size
        /// </summary>
        public Int32 Size { get; private set; }

        /// <summary>
        /// MD5 of the file
        /// </summary>
        public String MD5 { get; private set; }

        /// <summary>
        /// Duration of the voice message
        /// </summary>
        public Double Duration { get; private set; }

        public VoiceMailManagementEventArgs(String msgId, String fileId, String action, String url, String mimeType, String fileName, String size, String md5, String duration)
        {
            Id = msgId;
            FileId = fileId;
            Action = action;
            Url = url;
            MimeType = mimeType;
            FileName = fileName;
            MD5 = md5;

            Int32 val = 0;
            if (size != null)
            {
                Int32.TryParse(size, out val);
                Size = val;
            }

            Duration = 0;
            if (duration != null)
            {
                try
                {
                    Duration = Convert.ToDouble(duration, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                }
            }
        }

    }
}
