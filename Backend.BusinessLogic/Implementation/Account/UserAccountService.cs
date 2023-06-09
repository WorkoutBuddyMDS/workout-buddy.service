﻿using Microsoft.EntityFrameworkCore;
using Backend.DataAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using Backend.BusinessLogic.Base;
using Backend.Common.DTOs;
using Backend.Common.Extensions;
using Backend.Entities;
using Backend.Common.Exceptions;
using System.Text.RegularExpressions;
using Backend.Entities.Enums;

namespace Backend.BusinessLogic.Account
{
    public class UserAccountService : BaseService
    {
        private readonly RegisterUserValidator RegisterUserValidator;
        private readonly AddWeightValidator AddWeightValidator;
        private readonly EditProfileValidator EditProfileValidator;
        private readonly EditUserProfileValidator EditUserProfileValidator;

        public UserAccountService(ServiceDependencies dependencies)
            : base(dependencies)
        {
            RegisterUserValidator = new RegisterUserValidator(UnitOfWork);
            AddWeightValidator = new AddWeightValidator();
            EditProfileValidator = new EditProfileValidator(UnitOfWork);
            EditUserProfileValidator = new EditUserProfileValidator(UnitOfWork);
        }

        public async Task<CurrentUserDto> Login(LoginModel model)
        {
            var user = await UnitOfWork.Users
                .Get()
                .Include(u => u.Idroles)
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null)
            {
                return new CurrentUserDto { IsAuthenticated = false };
            }

            if (user.IsDeleted == true)
            {
                return new CurrentUserDto { IsDisabled = true };
            }

            if (!user.Password.SequenceEqual(model.Password.HashPassword(user.Salt)))
            {
                return new CurrentUserDto { IsAuthenticated = false };
            }

            var roles = user.Idroles.Select(r => r.Name).ToList();

            return new CurrentUserDto
            {
                Id = user.Iduser,
                Email = user.Email,
                Name = user.Name,
                Username = user.Username,
                IsAuthenticated = true,
                IsDisabled = user.IsDeleted ?? false,
                Roles = roles,
            };
        }

        public void RegisterNewUser(RegisterModel model)
        {
            ExecuteInTransaction(uow =>
            {
                RegisterUserValidator.Validate(model).ThenThrow(model);
                var userSalt = Guid.NewGuid();

                var user = new User()
                {
                    Iduser = Guid.NewGuid(),
                    Salt = Guid.NewGuid(),
                    Email = model.Email,
                    Name = model.Name,
                    BirthDate = (DateTime)model.BirthDay,
                    Username = model.Username
                };

                user.Password = model.PasswordString.HashPassword(user.Salt);
                var userRole = uow.Roles.Get().FirstOrDefault(r => r.Idrole == (int)RoleTypes.User);
                user.UserWeightHistories.Add(new UserWeightHistory()
                {
                    Iduser = user.Iduser,
                    WeighingDate = DateTime.UtcNow,
                    Weight = (double)model.Weight,
                    IduserNavigation = user,
                });

                user.LastLoginDate = DateTime.Now;

                user.Idroles.Add(userRole);


                uow.Users.Insert(user);

                uow.SaveChanges();
            });
        }
        public UserInfoModel GetUserInfo(Guid id)
        {
            var user = UnitOfWork.Users.Get()
                        .Include(u => u.UserPointsHistories)
                        .Include(u => u.UserWeightHistories)
                        .Include(u => u.Idroles)
                        .FirstOrDefault(u => u.Iduser == id);

            var userInfo = Mapper.Map<User, UserInfoModel>(user);
            userInfo.CurrentWeight = (float)user.UserWeightHistories
                                        .OrderByDescending(u => u.WeighingDate)
                                        .FirstOrDefault(u => u.Iduser == user.Iduser)
                                        .Weight;

            return userInfo;
        }

        public EditProfileModel GetEditModel(Guid id)
        {
            var user = UnitOfWork.Users.Get()
                        .First(u => u.Iduser == id);

            var model = Mapper.Map<User, EditProfileModel>(user);
            return model;
        }

        public void EditProfile(EditProfileModel model, Guid id)
        {
            ExecuteInTransaction(uow =>
            {
                EditProfileValidator.Validate(model).ThenThrow(model);

                var user = UnitOfWork.Users.Get()
                        .First(u => u.Iduser == id);

                Mapper.Map<EditProfileModel, User>(model, user);


                uow.Users.Update(user);

                uow.SaveChanges();
            });
        }

        public EditUserModel GetUserEditModel(Guid id)
        {
            var user = UnitOfWork.Users.Get()
                        .Include(u => u.Idroles)
                        .FirstOrDefault(u => u.Iduser == id);
            if (user == null)
            {
                throw new NotFoundErrorException("this user does not exist!");
            }
            var userModel = Mapper.Map<User, EditUserModel>(user);
            userModel.Roles = user.Idroles.Select(r => r.Idrole).ToList();
            return userModel;
        }

        public void EditUserProfile(EditUserModel model)
        {
            ExecuteInTransaction(uow =>
            {
                EditUserProfileValidator.Validate(model).ThenThrow(model);

                var user = uow.Users.Get()
                        .Include(u => u.Idroles)
                        .FirstOrDefault(u => u.Iduser == model.UserId);

                Mapper.Map<EditUserModel, User>(model, user);
                user.Idroles.Clear();
                foreach (var role in model.Roles)
                {
                    var newRole = uow.Roles.Get().FirstOrDefault(r => r.Idrole == role);
                    user.Idroles.Add(newRole);
                }

                user.LastModifiedOn = DateTime.Now;
                uow.Users.Update(user);
                uow.SaveChanges();

            });
        }

        public bool ChangePassword(PasswordChangeModel model, Guid currentUserId)
        {
            var isValid = true;

            ExecuteInTransaction(uow =>
            {
                var user = uow.Users.Get()
                    .FirstOrDefault(u => u.Iduser == currentUserId);

                var oldPasswordHash = model.OldPassword.HashPassword(user.Salt);

                if (!PasswordRegexTest(model.OldPassword))
                {
                    isValid = false;
                }
                else if (!PasswordRegexTest(model.NewPassword) || !oldPasswordHash.SequenceEqual(user.Password))
                {
                    isValid = false;
                }
                else
                {
                    var passwordHash = model.NewPassword.HashPassword(user.Salt);
                    user.Password = passwordHash;

                    uow.Users.Update(user);
                    uow.SaveChanges();
                }

            });
            return isValid;
        }

        private bool PasswordRegexTest(string password)
        {
            if (password == null)
            {
                return false;
            }
            var pattern = @"(?=.*[a-z])(?=.*[A-Z])(?=.*\d)[A-Za-z\d]{8,}";
            Regex re = new Regex(pattern);
            var res = re.IsMatch(password);
            return res;
        }

    }
}
