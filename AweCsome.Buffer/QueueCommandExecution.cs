﻿using AweCsome.Buffer.Entities;
using AweCsome.Interfaces;

using log4net;

using Newtonsoft.Json;

using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AweCsome.Buffer
{
    public class QueueCommandExecution
    {
        private readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private LiteDbQueue _queue;
        private readonly IAweCsomeTable _aweCsomeTable;
        private Type _baseType;
        public static Exception LastException { get; set; }

  

        public QueueCommandExecution(LiteDbQueue queue, IAweCsomeTable awecsomeTable, Type baseType)
        {
            _queue = queue;
            _aweCsomeTable = awecsomeTable;
            _baseType = baseType;
        }

        private bool HasBeenDeletedBefore(Command command)
        {
            var allCommands = _queue.Read();
            var commandsFromElement = allCommands.Where(q => q.ItemId == command.ItemId && q.FullyQualifiedName == command.FullyQualifiedName);
            bool hasBeenDeleted = commandsFromElement.FirstOrDefault(q => q.Action == Command.Actions.Delete) != null;
            if (hasBeenDeleted) _log.Warn($"Will not run command {command}, because Item has been deleted");
            return hasBeenDeleted;
        }

        private bool HasBeenInsertedBefore(Command command)
        {
            var allCommands = _queue.Read();
            var commandsFromElement = allCommands.Where(q => q.ItemId == command.ItemId && q.FullyQualifiedName == command.FullyQualifiedName);
            bool hasBeenInserted = commandsFromElement.FirstOrDefault(q => q.Action == Command.Actions.Insert) != null;
            if (hasBeenInserted) _log.Warn($"Will not run command {command}, because Item has never been created in SharePoint");
            return hasBeenInserted;
        }

        public bool DeleteTable(Command command)
        {
            try
            {
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.DeleteTable<object>());
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, null);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool CreateTable(Command command)
        {
            try
            {
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.CreateTable<object>());
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, null);
                command.State = Command.States.Disabled;
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool Insert(Command command)
        {
            try
            {
                if (HasBeenDeletedBefore(command)) return true;
                object element = _queue.GetFromDbById(_baseType, command.FullyQualifiedName, command.ItemId.Value);
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.InsertItem<object>(element));
                int newId = (int)_queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { element });
                _queue.UpdateId(_baseType, command.FullyQualifiedName, command.ItemId.Value, newId);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool Delete(Command command)
        {
            try
            {
                if (HasBeenInsertedBefore(command)) return true;
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.DeleteItemById<object>(command.ItemId.Value));
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { command.ItemId.Value });
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool Update(Command command)
        {
            try
            {
                if (HasBeenDeletedBefore(command)) return true;
                object element = _queue.GetFromDbById(_baseType, command.FullyQualifiedName, command.ItemId.Value);
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.UpdateItem(element));
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { element });
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool Like(Command command)
        {
            try
            {
                if (HasBeenDeletedBefore(command)) return true;
                object element = _queue.GetFromDbById(_baseType, command.FullyQualifiedName, command.ItemId.Value);
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.Like<object>(0, 0));
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { command.ItemId.Value, (int)command.Parameters.First().Value });
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool Unlike(Command command)
        {
            try
            {
                if (HasBeenDeletedBefore(command)) return true;
                object element = _queue.GetFromDbById(_baseType, command.FullyQualifiedName, command.ItemId.Value);
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.Unlike<object>(0, 0));
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { command.ItemId.Value, (int)command.Parameters.First().Value });
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool Empty(Command command)
        {
            try
            {
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.Empty<object>());
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, null);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool AttachFileToItem(Command command)
        {
            try
            {
                if (HasBeenDeletedBefore(command)) return true;
                string attachmentId = (string)command.Parameters["AttachmentId"];
                object element = _queue.GetFromDbById(_baseType, command.FullyQualifiedName, command.ItemId.Value);
                var attachmentStream = _queue.GetAttachmentStreamFromDbById(attachmentId, out string filename, out BufferFileMeta meta);
                long fileSize = attachmentStream.Length;
                attachmentStream.Seek(0, SeekOrigin.Begin);

                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.AttachFileToItem<object>(command.ItemId.Value, filename, new MemoryStream()));
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { command.ItemId.Value, filename, attachmentStream });

                var attCollection = _queue.GetCollection<FileAttachment>();
                var att = attCollection.FindById(attachmentId);
                if (att != null)
                {
                    if (fileSize > Configuration.MaxLocalDocLibSize)
                    {
                        att.State = FileBase.AllowedStates.Server;
                        _queue.DeleteAttachmentFromDbWithoutSyncing(meta);
                    }
                    else
                    {
                        att.State = FileBase.AllowedStates.Local;
                    }
                    attCollection.Update(att);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool RemoveAttachmentFromItem(Command command)
        {
            try
            {
                if (HasBeenDeletedBefore(command)) return true;
                //       object element = _queue.GetFromDbById(_baseType, command.FullyQualifiedName, command.ItemId.Value);
                string filename = (string)command.Parameters["Filename"];
                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.DeleteFileFromItem<object>(command.ItemId.Value, filename));
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { command.ItemId.Value, filename });
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool AttachFileToLibrary(Command command)
        {
            try
            {
                if (HasBeenDeletedBefore(command)) return true;
                string attachmentId = (string)command.Parameters["AttachmentId"];
                var attachmentStream = _queue.GetAttachmentStreamFromDbById(attachmentId, out string filename, out BufferFileMeta meta);
                long fileSize = attachmentStream.Length;
                string folder = meta.Folder;
                object element = null;
                if (!string.IsNullOrEmpty(meta.AdditionalInformation))
                {
                    Type targetType = _baseType.Assembly.GetType(command.FullyQualifiedName);
                    element = JsonConvert.DeserializeObject(meta.AdditionalInformation, targetType);
                }

                using (var saveStream = new MemoryStream(attachmentStream.ToArray()))
                {
                    MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.AttachFileToLibrary(folder, filename, saveStream, element));
                    _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { folder, filename, saveStream, element });
                }

                var attCollection = _queue.GetCollection<FileDoclib>();
                var att = attCollection.FindById(attachmentId);
                if (att != null)
                {
                    if (fileSize > Configuration.MaxLocalDocLibSize)
                    {
                        att.State = FileBase.AllowedStates.Server;
                        _queue.DeleteAttachmentFromDbWithoutSyncing(meta);
                    }
                    else
                    {
                        att.State = FileBase.AllowedStates.Local;
                    }
                    attCollection.Update(att);
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }

        public bool RemoveFileFromLibrary(Command command)
        {
            try
            {
                if (HasBeenDeletedBefore(command)) return true;
                //    object element = _queue.GetFromDbById(_baseType, command.FullyQualifiedName, command.ItemId.Value);
                string folder = (string)command.Parameters["Folder"];
                string filename = (string)command.Parameters["Filename"];

                MethodInfo method = _queue.GetMethod<IAweCsomeTable>(q => q.DeleteFilesFromDocumentLibrary<object>(folder, new System.Collections.Generic.List<string> { filename }));
                _queue.CallGenericMethodByName(_aweCsomeTable, method, _baseType, command.FullyQualifiedName, new object[] { folder, new System.Collections.Generic.List<string> { filename } });
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                LastException = ex;
                return false;
            }
        }
    }
}