using System;

namespace Sharp.Xmpp.Extensions
{
    public class RoomManagementEventArgs : EventArgs
    {
        /// <summary>
        /// The room  id 
        /// </summary>
        public String RoomId
        {
            get;
            private set;
        }

        /// <summary>
        /// The room jid
        /// </summary>
        public String RoomJid
        {
            get;
            private set;
        }

        /// <summary>
        /// UserJid (if any)
        /// </summary>
        public String UserJid
        {
            get;
            private set;
        }

        /// <summary>
        /// VCard action: updated
        /// </summary>
        public String VCard
        {
            get;
            private set;
        }

        /// <summary>
        /// User status (if any) accepted / invited / unsubscribed / deleted
        /// </summary>
        public String Status
        {
            get;
            private set;
        }

        /// <summary>
        /// User privilege (if any) user / moderator / guest
        /// </summary>
        public String Privilege
        {
            get;
            private set;
        }


        /// <summary>
        /// Room's updated name (if any)
        /// </summary>
        public String Name
        {
            get;
            private set;
        }

        /// <summary>
        /// Room's updated topic (if any)
        /// </summary>
        public String Topic
        {
            get;
            private set;
        }

        /// <summary>
        /// Avatar action (if any: delete / update)
        /// </summary>
        public String AvatarAction
        {
            get;
            private set;
        }

        /// <summary>
        /// Last avatar update date (if any)
        /// </summary>
        public String LastAvatarUpdateDate
        {
            get;
            private set;
        }

        /// <summary>
        /// possible values: lock / unlock
        /// </summary>
        public String AutoRegister
        {
            get;
            private set;
        }


        /// <summary>
        /// Initializes a new instance of the RoomManagementEventArgs class.
        /// </summary>
        /// <param name="roomId">the room id</param>
        /// <param name="roomJid">the room jid</param>
        /// <param name="userJid">the user JID</param>
        /// <param name="status">user status</param>
        /// <param name="name">the room name</param>
        /// <param name="topic">the room topic</param>
        /// <param name="lastAvatarUpdateDate">the last avatar romm update date</param>
        /// <param name="vcard">vcard action</param>
        /// <summary>
        public RoomManagementEventArgs(String roomId, String roomJid, String userJid, String status, String privilege, String name, String topic, String lastAvatarUpdateDate, String avatarAction, String vcard, String autoRegister)
        {
            RoomId = roomId;
            RoomJid = roomJid;
            UserJid = userJid;
            Status = status;
            Privilege = privilege;
            Name = name;
            Topic = topic;
            LastAvatarUpdateDate = lastAvatarUpdateDate;
            AvatarAction = avatarAction;
            VCard = vcard;
            AutoRegister = autoRegister;
        }
    }
}
