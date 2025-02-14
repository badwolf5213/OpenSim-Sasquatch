/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Data;
using System.Reflection;
using System.Collections.Generic;
using System.Data.SQLite;

using log4net;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Data.SQLite
{
    /// <summary>
    /// A SQLite Interface for the Asset Server
    /// </summary>
    public class SQLiteXInventoryData : IXInventoryData
    {
//        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private SqliteFolderHandler m_Folders;
        private SqliteItemHandler m_Items;

        public SQLiteXInventoryData(string conn, string realm)
        {
            if (Util.IsWindows())
                Util.LoadArchSpecificWindowsDll("sqlite3.dll");

            m_Folders = new SqliteFolderHandler(
                    conn, "inventoryfolders", "XInventoryStore");
            m_Items = new SqliteItemHandler(
                    conn, "inventoryitems", String.Empty);
        }

        public XInventoryFolder[] GetFolders(string[] fields, string[] vals)
        {
            return m_Folders.Get(fields, vals);
        }

        public XInventoryItem[] GetItems(string[] fields, string[] vals)
        {
            return m_Items.Get(fields, vals);
        }
        public XInventoryItem[] GetItems(string field, string[] vals)
        {
            return m_Items.Get(field, vals);
        }
        public XInventoryItem[] GetItems(string field, string val)
        {
            return m_Items.Get(field, val);
        }

        public bool StoreFolder(XInventoryFolder folder)
        {
            if (folder.folderName.Length > 64)
                folder.folderName = folder.folderName.Substring(0, 64);

            return m_Folders.Store(folder);
        }

        public bool StoreItem(XInventoryItem item)
        {
            if (item.inventoryName.Length > 64)
                item.inventoryName = item.inventoryName.Substring(0, 64);
            if (item.inventoryDescription.Length > 128)
                item.inventoryDescription = item.inventoryDescription.Substring(0, 128);

            return m_Items.Store(item);
        }

        public bool DeleteFolders(string field, string val)
        {
            return m_Folders.Delete(field, val);
        }

        public bool DeleteFolders(string[] fields, string[] vals)
        {
            return m_Folders.Delete(fields, vals);
        }

        public bool DeleteItems(string field, string val)
        {
            return m_Items.Delete(field, val);
        }

        public bool DeleteItems(string[] fields, string[] vals)
        {
            return m_Items.Delete(fields, vals);
        }

        public bool MoveItem(string id, string newParent)
        {
            return m_Items.MoveItem(id, newParent);
        }

        public bool MoveItems(string[] ids, string[] newParents)
        {
            for(int i = 0; i< ids.Length; ++i)            
                m_Items.MoveItem(ids[i], newParents[i]);
            return true;
        }

        public bool MoveFolder(string id, string newParent)
        {
            return m_Folders.MoveFolder(id, newParent);
        }

        public XInventoryItem[] GetActiveGestures(UUID principalID)
        {
            return m_Items.GetActiveGestures(principalID);
        }

        public int GetAssetPermissions(UUID principalID, UUID assetID)
        {
            return m_Items.GetAssetPermissions(principalID, assetID);
        }
    }

    public class SqliteItemHandler : SqliteInventoryHandler<XInventoryItem>
    {
        public SqliteItemHandler(string c, string t, string m) :
                base(c, t, m)
        {
        }

        public override bool Store(XInventoryItem item)
        {
            if (!base.Store(item))
                return false;

            IncrementFolderVersion(item.parentFolderID);

            return true;
        }

        public override bool Delete(string field, string val)
        {
            XInventoryItem[] retrievedItems = Get(new string[] { field }, new string[] { val });
            if (retrievedItems.Length == 0)
                return false;

            if (!base.Delete(field, val))
                return false;

            // Don't increment folder version here since Delete(string, string) calls Delete(string[], string[])
//            IncrementFolderVersion(retrievedItems[0].parentFolderID);

            return true;
        }

        public override bool Delete(string[] fields, string[] vals)
        {
            XInventoryItem[] retrievedItems = Get(fields, vals);
            if (retrievedItems.Length == 0)
                return false;

            if (!base.Delete(fields, vals))
                return false;

            HashSet<UUID> deletedItemFolderUUIDs = new HashSet<UUID>();

            Array.ForEach<XInventoryItem>(retrievedItems, i => deletedItemFolderUUIDs.Add(i.parentFolderID));

            foreach (UUID deletedItemFolderUUID in deletedItemFolderUUIDs)
                IncrementFolderVersion(deletedItemFolderUUID);

            return true;
        }

        public bool MoveItem(string id, string newParent)
        {
            XInventoryItem[] retrievedItems = Get(new string[] { "inventoryID" }, new string[] { id });
            if (retrievedItems.Length == 0)
                return false;

            UUID oldParent = retrievedItems[0].parentFolderID;

            using (SQLiteCommand cmd = new SQLiteCommand())
            {
                cmd.CommandText = String.Format("update {0} set parentFolderID = :ParentFolderID where inventoryID = :InventoryID", m_Realm);
                cmd.Parameters.Add(new SQLiteParameter(":ParentFolderID", newParent));
                cmd.Parameters.Add(new SQLiteParameter(":InventoryID", id));

                if (ExecuteNonQuery(cmd, m_Connection) == 0)
                    return false;
            }

            IncrementFolderVersion(oldParent);
            IncrementFolderVersion(newParent);

            return true;
        }

        public XInventoryItem[] GetActiveGestures(UUID principalID)
        {
            using (SQLiteCommand cmd  = new SQLiteCommand())
            {
                cmd.CommandText = String.Format("select * from inventoryitems where avatarId = :uuid and assetType = :type and flags = 1", m_Realm);

                cmd.Parameters.Add(new SQLiteParameter(":uuid", principalID.ToString()));
                cmd.Parameters.Add(new SQLiteParameter(":type", (int)AssetType.Gesture));

                return DoQuery(cmd);
            }
        }

        public int GetAssetPermissions(UUID principalID, UUID assetID)
        {
            IDataReader reader;

            using (SQLiteCommand cmd = new SQLiteCommand())
            {
                cmd.CommandText = String.Format("select inventoryCurrentPermissions from inventoryitems where avatarID = :PrincipalID and assetID = :AssetID", m_Realm);
                cmd.Parameters.Add(new SQLiteParameter(":PrincipalID", principalID.ToString()));
                cmd.Parameters.Add(new SQLiteParameter(":AssetID", assetID.ToString()));

                reader = ExecuteReader(cmd, m_Connection);
            }

            int perms = 0;

            while (reader.Read())
            {
                perms |= Convert.ToInt32(reader["inventoryCurrentPermissions"]);
            }

            reader.Close();
            //CloseCommand(cmd);

            return perms;
        }
    }

    public class SqliteFolderHandler : SqliteInventoryHandler<XInventoryFolder>
    {
        public SqliteFolderHandler(string c, string t, string m) :
                base(c, t, m)
        {
        }

        public override bool Store(XInventoryFolder folder)
        {
            if (!base.Store(folder))
                return false;

            IncrementFolderVersion(folder.parentFolderID);

            return true;
        }

        public bool MoveFolder(string id, string newParentFolderID)
        {
            XInventoryFolder[] folders = Get(new string[] { "folderID" }, new string[] { id });

            if (folders.Length == 0)
                return false;

            UUID oldParentFolderUUID = folders[0].parentFolderID;

            using (SQLiteCommand cmd = new SQLiteCommand())
            {
                cmd.CommandText = String.Format("update {0} set parentFolderID = :ParentFolderID where folderID = :FolderID", m_Realm);
                cmd.Parameters.Add(new SQLiteParameter(":ParentFolderID", newParentFolderID));
                cmd.Parameters.Add(new SQLiteParameter(":FolderID", id));

                if (ExecuteNonQuery(cmd, m_Connection) == 0)
                    return false;
            }

            IncrementFolderVersion(oldParentFolderUUID);
            IncrementFolderVersion(newParentFolderID);

            return true;
        }

    }

    public class SqliteInventoryHandler<T> : SQLiteGenericTableHandler<T> where T: class, new()
    {
        public SqliteInventoryHandler(string c, string t, string m) : base(c, t, m) {}

        protected bool IncrementFolderVersion(UUID folderID)
        {
            return IncrementFolderVersion(folderID.ToString());
        }

        protected bool IncrementFolderVersion(string folderID)
        {
//            m_log.DebugFormat("[MYSQL ITEM HANDLER]: Incrementing version on folder {0}", folderID);
//            Util.PrintCallStack();

            using (SQLiteCommand cmd = new SQLiteCommand())
            {
                cmd.CommandText = "update inventoryfolders set version=version+1 where folderID = :folderID";
                cmd.Parameters.Add(new SQLiteParameter(":folderID", folderID));

                if(ExecuteNonQuery(cmd, m_Connection) == 0)
                    return false;
            }

            return true;
        }
    }
}