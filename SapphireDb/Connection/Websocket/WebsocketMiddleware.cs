﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SapphireDb.Models;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SapphireDb.Command;
using SapphireDb.Command.Connection;
using SapphireDb.Helper;
using SapphireDb.Internal;

namespace SapphireDb.Connection.Websocket
{
    class WebsocketMiddleware
    {
        private readonly ConnectionManager connectionManager;
        private readonly SapphireDatabaseOptions options;

        // ReSharper disable once UnusedParameter.Local
        public WebsocketMiddleware(RequestDelegate next, ConnectionManager connectionManager,
            SapphireDatabaseOptions options)
        {
            this.connectionManager = connectionManager;
            this.options = options;
        }

        public async Task Invoke(HttpContext context, CommandExecutor commandExecutor, IServiceProvider serviceProvider,
            ILogger<WebsocketConnection> logger)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();

                if (!AuthHelper.CheckApiAuth(context.Request.Query["key"], context.Request.Query["secret"],
                    options))
                {
                    await webSocket.Send(new WrongApiResponse());
                    await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Wrong API key or secret",
                        CancellationToken.None);
                    return;
                }

                WebsocketConnection connection = new WebsocketConnection(webSocket, context);

                connectionManager.AddConnection(connection);
                await connection.Send(new ConnectionResponse()
                {
                    ConnectionId = connection.Id
                });

                while (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.Connecting)
                {
                    try
                    {
                        string message = await connection.Websocket.Receive();

                        if (!string.IsNullOrEmpty(message))
                        {
                            _ = Task.Run(async () =>
                            {
                                CommandBase command = JsonHelper.DeserializeCommand(message);

                                if (command != null)
                                {
                                    ResponseBase response = await commandExecutor.ExecuteCommand(command,
                                        serviceProvider.CreateScope().ServiceProvider, connection.Information,
                                        logger,
                                        connection);

                                    if (response != null)
                                    {
                                        await connection.Send(response);
                                    }
                                }
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (WebSocketException)
                    {
                        // Ignore
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex.Message);
                    }
                }

                connectionManager.RemoveConnection(connection);
            }
        }
    }
}