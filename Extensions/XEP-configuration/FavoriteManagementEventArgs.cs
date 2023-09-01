using System;

namespace Sharp.Xmpp.Extensions

{
    public class FavoriteManagementEventArgs : EventArgs
    {
        /// <summary>
        /// The favorite  id 
        /// </summary>
        public String Id
        {
            get;
            private set;
        }

        /// <summary>
        /// The favorite management's action create / update / delete
        /// </summary>
        public String Action
        {
            get;
            private set;
        }

        /// <summary>
        /// Type of the favorite
        /// </summary>
        public String Type
        {
            get;
            private set;
        }

        /// <summary>
        /// Peer ID of the favorite
        /// </summary>
        public String PeerId
        {
            get;
            private set;
        }

        /// <summary>
        /// Position of the favorite
        /// </summary>
        public int Position
        {
            get;
            private set;
        }

        /// <summary>
        /// Initializes a new instance of the FavoriteManagementEventArgs class.
        /// </summary>
        /// <param name="id">the favorite id</param>
        /// <param name="action">the action about this favorite</param>
        /// <param name="type">the favorite's type</param>
        /// <param name="position">the favorite's status</param>
        /// <param name="peerId">the favorite's peerId</param>
        /// <summary>
        public FavoriteManagementEventArgs(String id, String action, String type, String position, String peerId)
        {
            Id = id;
            Action = action;
            Type = type;
            PeerId = peerId;

            if (Int32.TryParse(position, out int val))
                Position = val;
            else
                Position = 0;
        }
    }
}
