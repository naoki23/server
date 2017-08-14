﻿using IdentityServer4.Services;
using System.Threading.Tasks;
using IdentityServer4.Models;
using Bit.Core.Repositories;
using Bit.Core.Services;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
using System;
using IdentityModel;

namespace Bit.Core.IdentityServer
{
    public class ProfileService : IProfileService
    {
        private readonly IUserService _userService;
        private readonly IUserRepository _userRepository;
        private readonly IOrganizationUserRepository _organizationUserRepository;
        private readonly ILicensingService _licensingService;

        public ProfileService(
            IUserRepository userRepository,
            IUserService userService,
            IOrganizationUserRepository organizationUserRepository,
            ILicensingService licensingService)
        {
            _userRepository = userRepository;
            _userService = userService;
            _organizationUserRepository = organizationUserRepository;
            _licensingService = licensingService;
        }

        public async Task GetProfileDataAsync(ProfileDataRequestContext context)
        {
            var existingClaims = context.Subject.Claims;
            var newClaims = new List<Claim>();

            var user = await _userService.GetUserByPrincipalAsync(context.Subject);
            if(user != null)
            {
                newClaims.AddRange(new List<Claim>
                {
                    new Claim("premium", isPremium ? "true" : "false", ClaimValueTypes.Boolean),
                    new Claim(JwtClaimTypes.Email, user.Email),
                    new Claim(JwtClaimTypes.EmailVerified, user.EmailVerified ? "true" : "false", ClaimValueTypes.Boolean),
                    new Claim("sstamp", user.SecurityStamp)
                });

                if(!string.IsNullOrWhiteSpace(user.Name))
                {
                    newClaims.Add(new Claim(JwtClaimTypes.Name, user.Name));
                }

                // Orgs that this user belongs to
                var orgs = await _organizationUserRepository.GetManyByUserAsync(user.Id);
                if(orgs.Any())
                {
                    var groupedOrgs = orgs.Where(o => o.Status == Enums.OrganizationUserStatusType.Confirmed)
                        .GroupBy(o => o.Type);

                    foreach(var group in groupedOrgs)
                    {
                        switch(group.Key)
                        {
                            case Enums.OrganizationUserType.Owner:
                                foreach(var org in group)
                                {
                                    newClaims.Add(new Claim("orgowner", org.OrganizationId.ToString()));
                                }
                                break;
                            case Enums.OrganizationUserType.Admin:
                                foreach(var org in group)
                                {
                                    newClaims.Add(new Claim("orgadmin", org.OrganizationId.ToString()));
                                }
                                break;
                            case Enums.OrganizationUserType.User:
                                foreach(var org in group)
                                {
                                    newClaims.Add(new Claim("orguser", org.OrganizationId.ToString()));
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
            }

            // filter out any of the new claims
            var existingClaimsToKeep = existingClaims
                .Where(c => !c.Type.StartsWith("org") && (newClaims.Count == 0 || !newClaims.Any(nc => nc.Type == c.Type)))
                .ToList();

            newClaims.AddRange(existingClaimsToKeep);
            if(newClaims.Any())
            {
                context.AddRequestedClaims(newClaims);
            }
        }

        public async Task IsActiveAsync(IsActiveContext context)
        {
            var securityTokenClaim = context.Subject?.Claims.FirstOrDefault(c => c.Type == "sstamp");
            var user = await _userService.GetUserByPrincipalAsync(context.Subject);

            if(user != null && securityTokenClaim != null)
            {
                context.IsActive = string.Equals(user.SecurityStamp, securityTokenClaim.Value,
                    StringComparison.InvariantCultureIgnoreCase);
                return;
            }
            else
            {
                context.IsActive = true;
            }
        }
    }
}
