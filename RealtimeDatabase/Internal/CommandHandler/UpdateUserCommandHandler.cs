﻿using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using RealtimeDatabase.Models.Commands;
using RealtimeDatabase.Models.Responses;
using RealtimeDatabase.Websocket.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RealtimeDatabase.Internal.CommandHandler
{
    class UpdateUserCommandHandler : AuthCommandHandlerBase, ICommandHandler<UpdateUserCommand>
    {
        private readonly AuthDbContextTypeContainer contextTypeContainer;
        private readonly RoleManager<IdentityRole> roleManager;

        public UpdateUserCommandHandler(AuthDbContextAccesor authDbContextAccesor, 
            AuthDbContextTypeContainer contextTypeContainer, IServiceProvider serviceProvider, RoleManager<IdentityRole> roleManager)
            : base(authDbContextAccesor, serviceProvider)
        {
            this.contextTypeContainer = contextTypeContainer;
            this.roleManager = roleManager;
        }

        public async Task Handle(WebsocketConnection websocketConnection, UpdateUserCommand command)
        {
            IRealtimeAuthContext context = GetContext();
            dynamic usermanager = serviceProvider.GetService(contextTypeContainer.UserManagerType);

            try
            {
                dynamic user = await usermanager.FindByIdAsync(command.Id);

                if (user != null)
                {
                    if (!String.IsNullOrEmpty(command.Email))
                    {
                        user.Email = command.Email;
                    }

                    if (!String.IsNullOrEmpty(command.UserName))
                    {
                        user.UserName = command.UserName;
                    }

                    if (command.AdditionalData != null)
                    {
                        foreach (KeyValuePair<string, JValue> keyValue in command.AdditionalData)
                        {
                            PropertyInfo pi = contextTypeContainer.UserType.GetProperty(keyValue.Key,
                                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                            if (pi != null && pi.DeclaringType == contextTypeContainer.UserType)
                            {
                                pi.SetValue(user, keyValue.Value.ToObject(pi.PropertyType));
                            }
                        }
                    }

                    if (!String.IsNullOrEmpty(command.Password))
                    {
                        user.PasswordHash = usermanager.PasswordHasher.HashPassword(user, command.Password);
                    }

                    IdentityResult result = await usermanager.UpdateAsync(user);

                    if (result.Succeeded)
                    {
                        if (command.Roles != null)
                        {
                            List<string> originalRoles =
                                await (dynamic)contextTypeContainer.UserManagerType.GetMethod("GetRolesAsync").Invoke(usermanager, new object[] { user });
                            IEnumerable<string> newRoles = command.Roles.Where(r => !originalRoles.Any(or => or == r));
                            IEnumerable<string> deletedRoles = originalRoles.Where(or => !command.Roles.Any(r => r == or));

                            foreach (string roleStr in newRoles)
                            {
                                if (!await roleManager.RoleExistsAsync(roleStr))
                                {
                                    await roleManager.CreateAsync(new IdentityRole(roleStr));
                                }
                            }

                            await usermanager.RemoveFromRolesAsync(user, deletedRoles);
                            await usermanager.AddToRolesAsync(user, newRoles);
                        }


                        Dictionary<string, object> newUserData = ModelHelper.GenerateUserData(user);
                        newUserData["Roles"] =
                            await (dynamic)contextTypeContainer.UserManagerType.GetMethod("GetRolesAsync").Invoke(usermanager, new object[] { user });

                        await SendMessage(websocketConnection, new UpdateUserResponse()
                        {
                            ReferenceId = command.ReferenceId,
                            NewUser = newUserData
                        });
                    }
                    else
                    {
                        await SendMessage(websocketConnection, new UpdateUserResponse()
                        {
                            ReferenceId = command.ReferenceId,
                            IdentityErrors = result.Errors
                        });
                    }
                }
                else
                {
                    await SendMessage(websocketConnection, new UpdateUserResponse()
                    {
                        Error = new Exception("No user with this id was found.")
                    });
                }
            }
            catch (Exception ex)
            {
                await SendMessage(websocketConnection, new CreateUserResponse()
                {
                    ReferenceId = command.ReferenceId,
                    Error = ex
                });
            }
        }
    }
}
