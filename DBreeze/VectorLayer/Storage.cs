﻿/* 
  Copyright (C) 2012 dbreeze.tiesky.com / Oleksiy Solovyov / Ivars Sudmalis.
  It's a free software for those who think that it should be free.
*/
using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace DBreeze.VectorLayer
{
    /// <summary>
    /// Emulates a Storage of Nodes in DBreeze, 
    /// !!!!!!!!!!!!! implement via IFace, to connect different storages
    /// </summary>
    internal class Storage
    {
        /* DBreeze Definition
           1.ToIndex() - VectorStat (holds monotonic counter/EntryPoint and other info)           
           3.ToIndex(itemId);V: serialized and Brotli compressed Node
           5.ToIndex(byte[] - external documentId); Value: (long) - ExternalId to internal nodeId

        */

        //long id = -1;

        VectorStat VStat = null;

        DBreeze.Transactions.Transaction tran = null;
        string tableName = null;

        public Storage(DBreeze.Transactions.Transaction tran, string tableName)
        {
            this.tran = tran;
            this.tableName = tableName;

            GetVectorStat();
        }

        ///// <summary>
        ///// !!!!!!!!!!!!!!  Interfaced to DB etc
        ///// !!! public temp for testing
        ///// </summary>
        //public Dictionary<long, Node> nodesStorage = new Dictionary<long, Node>();

        Node EntryNode = null;
        bool EntryNodeChanged = false;

        /// <summary>
        /// Interfaced 
        /// </summary>
        /// <returns></returns>
        public long GetNewId()
        {
            VStat.Id++;
            VStat.Changed = true;
            return VStat.Id;
        }

        /// <summary>
        /// Force read from DB, when not yet initialized
        /// </summary>
        /// <returns></returns>
        private VectorStat GetVectorStat()
        {
            if (VStat == null)
            {
                var row = tran.Select<byte[], byte[]>(tableName, 1.ToIndex());
                if (!row.Exists)
                    VStat = new VectorStat();
                else
                    VStat = VectorStat.BiserDecode(row.Value);
            }

            return VStat;
        }

        /// <summary>
        ///  Never returns NULL
        ///  
        /// </summary>
        /// <returns></returns>
        public Node GetEntryNode(bool forInsert=false)
        {
            if (EntryNode == null)
            {               
                if (VStat.EntryNodeId != -1)
                {
                    EntryNode = GetNodeById(VStat.EntryNodeId);
                }

                if (forInsert && EntryNode == null)
                {
                    EntryNode = new Node()
                    {
                        NodeType = Node.eType.Centroid,
                        Id = GetNewId(),
                        HoldsVectors = true
                    };

                    EntryNodeChanged = true;
                    VStat.Changed = true;
                    VStat.EntryNodeId = EntryNode.Id;

                    //-only for the first time, when not from DB
                    ChangedNodes[EntryNode.Id] = EntryNode;
                }
            }

            if (EntryNode == null)
                return new Node() { NodeType = Node.eType.Centroid };
            return EntryNode;
        }

        /// <summary>
        /// 
        /// </summary>
        public void SetEntryNode(Node entryNode)
        {           
            VStat.EntryNodeId = entryNode.Id;
            VStat.Changed = true;
            EntryNodeChanged = true;
            EntryNode = entryNode;
        }


        /// <summary>
        /// Cached nodes just help to speed up the process, they can be duplicated with changedNodes
        /// </summary>
        public Dictionary<long, Node> ChangedNodes = new Dictionary<long, Node>();
        Dictionary<long, Node> CachedNodes = new Dictionary<long, Node>();

        /// <summary>
        /// can return Null
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Node GetNodeById(long id)
        {
            Node node = null;

            if (ChangedNodes.TryGetValue(id, out node))
                return node;

            if (CachedNodes.TryGetValue(id, out node))
                return node;

            var row = tran.Select<byte[], byte[]>(tableName, 3.ToIndex(id));

            if (row.Exists)
            {
                //var nodeBt = row.Value.DecompressBytesBrotliDBreeze();
                var nodeBt = row.Value.GZip_Decompress();
                node = Node.BiserDecode(nodeBt);
                //-putting to cache
                CachedNodes[id] = node;
                return node;
            }

            return null;
        }

        /// <summary>
        /// Interfaced
        /// </summary>
        public void SaveNodes()
        {
            if (EntryNode == null)
                return;

            if (EntryNodeChanged)
            {
                if (!ChangedNodes.ContainsKey(EntryNode.Id))
                {
                    ChangedNodes[EntryNode.Id] = EntryNode;
                }

                EntryNodeChanged = false;
            }

            //-Saving VSTAT
            if (VStat.Changed)
                tran.Insert<byte[], byte[]>(tableName, 1.ToIndex(), VStat.BiserEncoder().Encode());

            //-Saving all changed nodes
            foreach (var el in ChangedNodes.OrderBy(r=>r.Key))
            {
                //-reassigning cache
                CachedNodes[el.Key] = el.Value;
                //-Saving nodes to DB                
                var nodeBt = el.Value.BiserEncoder().Encode();
                nodeBt = nodeBt.GZip_Compress();
                //nodeBt = nodeBt.CompressBytesBrotliDBreeze();
                tran.Insert<byte[], byte[]>(tableName, 3.ToIndex(el.Key), nodeBt); //node self

                if(el.Value.ExternalId != null) //VectorNode only
                    tran.Insert<byte[], long>(tableName, 5.ToIndex(el.Value.ExternalId), el.Value.Id); //ExternalId to Node
            }

            ChangedNodes.Clear();           

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        public void RemoveNode(long id)
        {
            var node = GetNodeById(id);

            if (node == null)
                return;

            ChangedNodes.Remove(id);
            CachedNodes.Remove(id);

            tran.RemoveKey(tableName, 3.ToIndex(id));
            tran.RemoveKey(tableName, 5.ToIndex(node.ExternalId));

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="externalId"></param>
        public void RemoveNode(byte[] externalId)
        {
            var row = tran.Select<byte[], long>(tableName, 5.ToIndex(externalId));
            if (!row.Exists)
                return;

            RemoveNode(row.Value);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="externalId"></param>
        /// <returns></returns>
        public Node GetNodeByExternalId(byte[] externalId)
        {
            var row = tran.Select<byte[], long>(tableName, 5.ToIndex(externalId));
            if (!row.Exists)
                return null;

            return GetNodeById(row.Value);

        }

    }//eoc


    internal partial class Node : Biser.IEncoder
    {


        public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
        {
            Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


            encoder.Add((System.Int32)NodeType);
            encoder.Add(ParentNodeId);
            encoder.Add(ChildNodes, (r1) => {
                encoder.Add(r1);
            });
            if (Vector == null)
                encoder.Add((byte)1);
            else
            {
                encoder.Add((byte)0);
                for (int it2 = 0; it2 < Vector.Rank; it2++)
                    encoder.Add(Vector.GetLength(it2));
                foreach (var el3 in Vector)
                    encoder.Add(el3);
            }
            encoder.Add(ExternalId);
            encoder.Add(HoldsVectors);
            encoder.Add(Id);

            return encoder;
        }


        public static Node BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
        {
            Biser.Decoder decoder = null;
            if (extDecoder == null)
            {
                if (enc == null || enc.Length == 0)
                    return null;
                decoder = new Biser.Decoder(enc);
            }
            else
            {
                if (extDecoder.CheckNull())
                    return null;
                else
                    decoder = extDecoder;
            }

            Node m = new Node();



            m.NodeType = (eType)decoder.GetInt();
            m.ParentNodeId = decoder.GetLong();
            m.ChildNodes = decoder.CheckNull() ? null : new System.Collections.Generic.List<System.Int64>();
            if (m.ChildNodes != null)
            {
                decoder.GetCollection(() => {
                    var pvar1 = decoder.GetLong();
                    return pvar1;
                }, m.ChildNodes, true);
            }
            m.Vector = decoder.CheckNull() ? null : new System.Double[decoder.GetInt()];
            if (m.Vector != null)
            {
                for (int ard2_0 = 0; ard2_0 < m.Vector.GetLength(0); ard2_0++)
                {
                    m.Vector[ard2_0] = decoder.GetDouble();
                }
            }
            m.ExternalId = decoder.GetByteArray();
            m.HoldsVectors = decoder.GetBool();
            m.Id = decoder.GetLong();


            return m;
        }


    }

    internal partial class VectorStat
    {
        public long Id { get; set; } = -1;

        public int VectorDimension { get; set; } = -1;

        public long EntryNodeId { get; set; } = -1;



        /// <summary>
        /// Not for serialization
        /// </summary>
        public bool Changed = false;

    }

    internal partial class VectorStat : Biser.IEncoder
    {


        public Biser.Encoder BiserEncoder(Biser.Encoder existingEncoder = null)
        {
            Biser.Encoder encoder = new Biser.Encoder(existingEncoder);


            encoder.Add(Id);
            encoder.Add(VectorDimension);
            encoder.Add(EntryNodeId);


            return encoder;
        }


        public static VectorStat BiserDecode(byte[] enc = null, Biser.Decoder extDecoder = null)
        {
            Biser.Decoder decoder = null;
            if (extDecoder == null)
            {
                if (enc == null || enc.Length == 0)
                    return null;
                decoder = new Biser.Decoder(enc);
            }
            else
            {
                if (extDecoder.CheckNull())
                    return null;
                else
                    decoder = extDecoder;
            }

            VectorStat m = new VectorStat();


            m.Id = decoder.GetLong();
            m.VectorDimension = decoder.GetInt();
            m.EntryNodeId = decoder.GetLong();

            return m;
        }


    }

}
