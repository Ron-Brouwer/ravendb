//-----------------------------------------------------------------------
// <copyright file="IndexCreation.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Raven.NewClient.Abstractions.Data;
using Raven.NewClient.Abstractions.Logging;
using Raven.NewClient.Client.Connection;

using Raven.NewClient.Client.Document;
using Raven.NewClient.Client.Exceptions;
using Raven.NewClient.Client.Exceptions.Compilation;
using Raven.NewClient.Client.Indexing;
using Raven.NewClient.Data.Indexes;
using Raven.NewClient.Operations;
using Raven.NewClient.Operations.Databases.Indexes;
using Sparrow.Json;

namespace Raven.NewClient.Client.Indexes
{
    /// <summary>
    /// Helper class for creating indexes from implementations of <see cref="AbstractIndexCreationTask"/>.
    /// </summary>
    public static class IndexCreation
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexCreation));

        /// <summary>
        /// Creates the indexes found in the specified assembly.
        /// </summary>
        /// <param name="assemblyToScanForIndexingTasks">The assembly to scan for indexing tasks.</param>
        /// <param name="documentStore">The document store.</param>
        public static void CreateIndexes(Assembly assemblyToScan, IDocumentStore documentStore)
        {
            var indexCompilationExceptions = new List<IndexCompilationException>();
            try
            {
                var tasks = GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan)
                    .ToList();

                documentStore.ExecuteIndexes(tasks);
            }
            // For old servers that don't have the new endpoint for executing multiple indexes
            catch (Exception ex)
            {
                Log.InfoException("Could not create indexes in one shot (maybe using older version of RavenDB ?)", ex);
                foreach (var task in GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan))
                {
                    try
                    {
                        task.Execute(documentStore);
                    }
                    catch (IndexCompilationException e)
                    {
                        indexCompilationExceptions.Add(new IndexCompilationException("Failed to compile index name = " + task.IndexName, e));
                    }
                }
            }

            CreateTransformers(assemblyToScan, documentStore);

            if (indexCompilationExceptions.Any())
                throw new AggregateException("Failed to create one or more indexes. Please see inner exceptions for more details.", indexCompilationExceptions);
        }

        /// <summary>
        /// Creates the indexes found in the specified assembly.
        /// </summary>
        public static void CreateIndexes(Assembly assemblyToScan, IDocumentStore documentStore, DocumentConvention conventions)
        {
            var indexCompilationExceptions = new List<IndexCompilationException>();
            try
            {
                var tasks = GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan)
                    .ToList();

                var indexesToAdd = CreateIndexesToAdd(tasks, conventions);
                var putIndexesOperation = new PutIndexesOperation(indexesToAdd);
                ((DocumentStore) documentStore).Admin.Send(putIndexesOperation);
            }
            // For old servers that don't have the new endpoint for executing multiple indexes
            catch (Exception ex)
            {
                Log.InfoException("Could not create indexes in one shot (maybe using older version of RavenDB ?)", ex);
                foreach (var task in GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan))
                {
                    try
                    {
                        task.Execute((DocumentStoreBase)documentStore, conventions);
                    }
                    catch (IndexCompilationException e)
                    {
                        indexCompilationExceptions.Add(new IndexCompilationException("Failed to compile index name = " + task.IndexName, e));
                    }
                }
            }

            CreateTransformers(assemblyToScan, documentStore, conventions);

            if (indexCompilationExceptions.Any())
                throw new AggregateException("Failed to create one or more indexes. Please see inner exceptions for more details.", indexCompilationExceptions);
        }

        /// <summary>
        /// Creates the indexes found in the specified assembly.
        /// </summary>
        public static async Task CreateIndexesAsync(Assembly assemblyToScan, IDocumentStore documentStore)
        {
            var indexCompilationExceptions = new List<IndexCompilationException>();
            try
            {
                var tasks = GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan)
                    .ToList();

                await documentStore.ExecuteIndexesAsync(tasks).ConfigureAwait(false);
            }
            // For old servers that don't have the new endpoint for executing multiple indexes
            catch (Exception ex)
            {
                Log.InfoException("Could not create indexes in one shot (maybe using older version of RavenDB ?)", ex);
                foreach (var task in GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan))
                {
                    try
                    {
                        await task.ExecuteAsync(documentStore).ConfigureAwait(false);
                    }
                    catch (IndexCompilationException e)
                    {
                        indexCompilationExceptions.Add(new IndexCompilationException("Failed to compile index name = " + task.IndexName, e));
                    }
                }
            }

            await CreateTransformersAsync(assemblyToScan, documentStore).ConfigureAwait(false);

            if (indexCompilationExceptions.Any())
                throw new AggregateException("Failed to create one or more indexes. Please see inner exceptions for more details.", indexCompilationExceptions);
        }

        /// <summary>
        /// Creates the indexes found in the specified assembly.
        /// </summary>
        public static async Task CreateIndexesAsync(Assembly assemblyToScan, IDocumentStore documentStore, DocumentConvention conventions)
        {
            var indexCompilationExceptions = new List<IndexCompilationException>();
            try
            {
                var tasks = GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan)
                    .ToList();

                var indexesToAdd = CreateIndexesToAdd(tasks, conventions);
                var putIndexesOperation = new PutIndexesOperation(indexesToAdd);
                await ((DocumentStore)documentStore).Admin.SendAsync(putIndexesOperation).ConfigureAwait(false);
            }
            // For old servers that don't have the new endpoint for executing multiple indexes
            catch (Exception ex)
            {
                Log.InfoException("Could not create indexes in one shot (maybe using older version of RavenDB ?)", ex);
                foreach (var task in GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan))
                {
                    try
                    {
                        await task.ExecuteAsync((DocumentStoreBase)documentStore, conventions).ConfigureAwait(false);
                    }
                    catch (IndexCompilationException e)
                    {
                        indexCompilationExceptions.Add(new IndexCompilationException("Failed to compile index name = " + task.IndexName, e));
                    }
                }
            }

            await CreateTransformersAsync(assemblyToScan, documentStore, conventions).ConfigureAwait(false);

            if (indexCompilationExceptions.Any())
                throw new AggregateException("Failed to create one or more indexes. Please see inner exceptions for more details.", indexCompilationExceptions);
        }

        /// <summary>
        /// Creates the indexes found in the specified catalog in side-by-side mode.
        /// </summary>
        public static void SideBySideCreateIndexes(Assembly assemblyToScan, IDocumentStore documentStore, long? minimumEtagBeforeReplace = null)
        {
            var indexCompilationExceptions = new List<IndexCompilationException>();
            try
            {
                var tasks = GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan)
                    .ToList();

                documentStore.SideBySideExecuteIndexes(tasks, minimumEtagBeforeReplace);
            }
            // For old servers that don't have the new endpoint for executing multiple indexes
            catch (Exception ex)
            {
                Log.InfoException("Could not create side by side indexes in one shot (maybe using older version of RavenDB ?)", ex);
                foreach (var task in GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan))
                {
                    try
                    {
                        task.SideBySideExecute(documentStore, minimumEtagBeforeReplace);
                    }
                    catch (IndexCompilationException e)
                    {
                        indexCompilationExceptions.Add(new IndexCompilationException("Failed to compile side by side index name = " + task.IndexName, e));
                    }
                }
            }

            CreateTransformers(assemblyToScan, documentStore);

            if (indexCompilationExceptions.Any())
                throw new AggregateException("Failed to create one or more side by indexes. Please see inner exceptions for more details.", indexCompilationExceptions);
        }

        /// <summary>
        /// Creates the indexes found in the specified assembly in side-by-side mode.
        /// </summary>
        public static void SideBySideCreateIndexes(Assembly assemblyToScan, IDocumentStore documentStore, DocumentConvention conventions, long? minimumEtagBeforeReplace = null)
        {
            var documentStoreBase = (DocumentStoreBase) documentStore;
            var indexCompilationExceptions = new List<IndexCompilationException>();
            try
            {
                var tasks = GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan)
                    .ToList();

                var indexesToAdd = CreateIndexesToAdd(tasks, conventions, minimumEtagBeforeReplace);

                var requestExecuter = documentStoreBase.GetRequestExecuter();

                JsonOperationContext context;

                using (requestExecuter.ContextPool.AllocateOperationContext(out context))
                {
                    var admin = new AdminOperationExecuter(documentStoreBase, requestExecuter, context);
                    var putIndexesOperation = new PutIndexesOperation(indexesToAdd);
                    admin.Send(putIndexesOperation);
                }
            }
            // For old servers that don't have the new endpoint for executing multiple indexes
            catch (Exception)
            {
                foreach (var task in GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan))
                {
                    try
                    {
                        task.SideBySideExecute(documentStoreBase, conventions, minimumEtagBeforeReplace);
                    }
                    catch (IndexCompilationException e)
                    {
                        indexCompilationExceptions.Add(new IndexCompilationException("Failed to compile side by side index name = " + task.IndexName, e));
                    }
                }
            }

            CreateTransformers(assemblyToScan, documentStore, conventions);

            if (indexCompilationExceptions.Any())
                throw new AggregateException("Failed to create one or more side by side indexes. Please see inner exceptions for more details.", indexCompilationExceptions);
        }

        /// <summary>
        /// Creates the indexes found in the specified catalog in side-by-side mode.
        /// </summary>
        public static async Task SideBySideCreateIndexesAsync(Assembly assemblyToScan, IDocumentStore documentStore, long? minimumEtagBeforeReplace = null)
        {
            var indexCompilationExceptions = new List<IndexCompilationException>();
            try
            {
                var tasks = GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan)
                    .ToList();

                await documentStore.SideBySideExecuteIndexesAsync(tasks, minimumEtagBeforeReplace).ConfigureAwait(false);
            }
            // For old servers that don't have the new endpoint for executing multiple indexes
            catch (Exception ex)
            {
                Log.InfoException("Could not create side by side indexes in one shot (maybe using older version of RavenDB ?)", ex);
                foreach (var task in GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan))
                {
                    try
                    {
                        await task.SideBySideExecuteAsync(documentStore, minimumEtagBeforeReplace).ConfigureAwait(false);
                    }
                    catch (IndexCompilationException e)
                    {
                        indexCompilationExceptions.Add(new IndexCompilationException("Failed to compile side by side index name = " + task.IndexName, e));
                    }
                }
            }

            await CreateTransformersAsync(assemblyToScan, documentStore).ConfigureAwait(false);

            if (indexCompilationExceptions.Any())
                throw new AggregateException("Failed to create one or more side by indexes. Please see inner exceptions for more details.", indexCompilationExceptions);
        }

        /// <summary>
        /// Creates the indexes found in the specified assembly in side-by-side mode.
        /// </summary>
        public static async Task SideBySideCreateIndexesAsync(Assembly assemblyToScan, IDocumentStore documentStore, DocumentConvention conventions, long? minimumEtagBeforeReplace = null)
        {
            var documentStoreBase = (DocumentStoreBase)documentStore;
            var indexCompilationExceptions = new List<IndexCompilationException>();
            try
            {
                var tasks = GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan)
                    .ToList();

                var indexesToAdd = CreateIndexesToAdd(tasks, conventions, minimumEtagBeforeReplace);

                var requestExecuter = documentStoreBase.GetRequestExecuter();

                JsonOperationContext context;

                using (requestExecuter.ContextPool.AllocateOperationContext(out context))
                {
                    var admin = new AdminOperationExecuter(documentStoreBase, requestExecuter, context);
                    var putIndexOperation = new PutIndexesOperation(indexesToAdd);
                    await admin.SendAsync(putIndexOperation).ConfigureAwait(false);
                }
            }
            // For old servers that don't have the new endpoint for executing multiple indexes
            catch (Exception)
            {
                foreach (var task in GetAllInstancesOfType<AbstractIndexCreationTask>(assemblyToScan))
                {
                    try
                    {
                        await task.SideBySideExecuteAsync(documentStoreBase, conventions, minimumEtagBeforeReplace).ConfigureAwait(false);
                    }
                    catch (IndexCompilationException e)
                    {
                        indexCompilationExceptions.Add(new IndexCompilationException("Failed to compile side by side index name = " + task.IndexName, e));
                    }
                }
            }

            await CreateTransformersAsync(assemblyToScan, documentStore, conventions).ConfigureAwait(false);

            if (indexCompilationExceptions.Any())
                throw new AggregateException("Failed to create one or more side by side indexes. Please see inner exceptions for more details.", indexCompilationExceptions);
        }

        private static void CreateTransformers(Assembly assemblyToScan, IDocumentStore documentStore, DocumentConvention conventions)
        {
            foreach (var task in GetAllInstancesOfType<AbstractTransformerCreationTask>(assemblyToScan))
            {
                task.Execute(documentStore, conventions);
            }
        }

        private static async Task CreateTransformersAsync(Assembly assemblyToScan, IDocumentStore documentStore)
        {
            foreach (var task in GetAllInstancesOfType<AbstractTransformerCreationTask>(assemblyToScan))
            {
                await task.ExecuteAsync(documentStore).ConfigureAwait(false);
            }
        }

        private static async Task CreateTransformersAsync(Assembly assemblyToScan, IDocumentStore documentStore, DocumentConvention conventions)
        {
            foreach (var task in GetAllInstancesOfType<AbstractTransformerCreationTask>(assemblyToScan))
            {
                await task.ExecuteAsync(documentStore, conventions).ConfigureAwait(false);
            }
        }

        private static void CreateTransformers(Assembly assemblyToScan, IDocumentStore documentStore)
        {
            foreach (var task in GetAllInstancesOfType<AbstractTransformerCreationTask>(assemblyToScan))
            {
                task.Execute(documentStore);
            }
        }

        public static IndexToAdd[] CreateIndexesToAdd(IEnumerable<AbstractIndexCreationTask> indexCreationTasks, DocumentConvention conventions,
            long? minimumEtagBeforeReplace = null)
        {
            var indexesToAdd = indexCreationTasks
                .Select(x =>
                {
                    x.Conventions = conventions;
                    var definition = x.CreateIndexDefinition();
                    definition.MinimumEtagBeforeReplace = minimumEtagBeforeReplace;
                    return new IndexToAdd
                    {
                        Name = x.IndexName,
                        Definition = definition,
                        Priority = x.Priority ?? IndexPriority.Normal
                    };
                })
                .ToArray();

            return indexesToAdd;
        }

        private static IEnumerable<TType> GetAllInstancesOfType<TType>(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes()
                .Where(x => x.GetTypeInfo().IsClass && x.GetTypeInfo().IsAbstract == false && x.GetTypeInfo().IsSubclassOf(typeof(TType))))
            {
                yield return (TType)Activator.CreateInstance(type);
            }
        }
    }
}