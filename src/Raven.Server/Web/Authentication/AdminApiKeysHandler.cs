﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using NetTopologySuite.Noding;
using NetTopologySuite.Utilities;
using Raven.Abstractions.Data;
using Raven.Client.Data;
using Raven.Json.Linq;
using Raven.Server.Json;
using Raven.Server.Json.Parsing;
using Raven.Server.Routing;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Web.Authentication
{
    public class AdminApiKeysHandler : RequestHandler
    {
        [RavenAction("/admin/api-keys", "PUT", "/admin/api-keys?name={api-key-name:string}")]
        public Task PutApiKey()
        {
            TransactionOperationContext ctx;
            using (ServerStore.ContextPool.AllocateOperationContext(out ctx))
            {
                var name = HttpContext.Request.Query["name"];

                if (name.Count != 1)
                {
                    HttpContext.Response.StatusCode = 400;
                    return HttpContext.Response.WriteAsync("'name' query string must have exactly one value");
                }

                var apiKey = ctx.ReadForDisk(RequestBodyStream(), name[0]);

                //TODO: Validate API Key Structure

                using (var tx = ctx.OpenWriteTransaction())
                {
                    ServerStore.Write(ctx, Constants.ApiKeyPrefix + name[0], apiKey);

                    tx.Commit();
                }
                AccessToken value;
                if (Server.AccessTokensByName.TryRemove(name[0], out value))
                {
                    Server.AccessTokensById.TryRemove(value.Token, out value);
                }
                return Task.CompletedTask;
            }
        }

        [RavenAction("/admin/api-keys", "GET", "/admin/api-keys?name={api-key-name:string}")]
        public Task GetApiKey()
        {
            TransactionOperationContext ctx;
            using (ServerStore.ContextPool.AllocateOperationContext(out ctx))
            {
                var name = HttpContext.Request.Query["name"];

                if (name.Count != 1)
                {
                    HttpContext.Response.StatusCode = 400;
                    return HttpContext.Response.WriteAsync("'name' query string must have exactly one value");
                }

                ctx.OpenReadTransaction();

                var apiKey = ServerStore.Read(ctx, Constants.ApiKeyPrefix + name[0]);

                if (apiKey == null)
                {
                    HttpContext.Response.StatusCode = 404;
                    return Task.CompletedTask;
                }

                HttpContext.Response.StatusCode = 200;

                ctx.Write(ResponseBodyStream(), apiKey);

                return Task.CompletedTask;
            }
        }

        [RavenAction("/admin/api-keys", "DELETE", "/admin/api-keys?name={api-key-name:string}")]
        public Task DeleteApiKey()
        {
            TransactionOperationContext ctx;
            using (ServerStore.ContextPool.AllocateOperationContext(out ctx))
            {
                var name = HttpContext.Request.Query["name"];

                if (name.Count != 1)
                {
                    HttpContext.Response.StatusCode = 400;
                    return HttpContext.Response.WriteAsync("'name' query string must have exactly one value");
                }

                using (var tx = ctx.OpenWriteTransaction())
                {
                    ServerStore.Delete(ctx, Constants.ApiKeyPrefix + name[0]);

                    tx.Commit();
                }
                AccessToken value;
                if (Server.AccessTokensByName.TryRemove(name[0], out value))
                {
                    Server.AccessTokensById.TryRemove(value.Token, out value);
                }
                return Task.CompletedTask;
            }
        }


        [RavenAction("/admin/stream-apikeys", "GET", "/admin/stream-apikeys", NoAuthorizationRequired = true)]
        public Task OauthStreamAllGetApiKey()
        {
            var start = GetStart();
            var page = GetPageSize();

            TransactionOperationContext context;
            using (ServerStore.ContextPool.AllocateOperationContext(out context))
            {
                context.OpenReadTransaction();


                using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    writer.WriteStartArray();
                    bool first = true;
                    foreach (var item in ServerStore.StartingWith(context, Constants.ApiKeyPrefix, start, page))
                    {
                        if (first == false)
                            writer.WriteComma();
                        else
                            first = false;

                        string username = item.Key.Substring(Constants.ApiKeyPrefix.Length);

                        item.Data.Modifications = new DynamicJsonValue(item.Data)
                        {
                            ["UserName"] = username,
                        };
                        context.Write(writer, item.Data);
                    }
                    writer.WriteEndArray();

                    }
            }

            return Task.CompletedTask;
        }
    }
}