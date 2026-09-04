namespace AMD.AutoService.GaragePro.Infrastructure.Legacy;

/// <summary>
/// Legacy Customer/Car have no BranchId. Scope them through the recording staff,
/// a branch-recorded ownership link, or an existing job in the current branch.
/// The legacy sharing flag must never broaden these management APIs.
/// </summary>
internal static class BranchDataScope
{
    internal const string CustomerIds = """
        SELECT owned.Id FROM Customer owned WITH (READUNCOMMITTED)
        JOIN [User] scopeUser WITH (READUNCOMMITTED) ON scopeUser.Id = owned.LastUserId
        JOIN Staff scopeStaff WITH (READUNCOMMITTED) ON scopeStaff.Id = scopeUser.StaffId
        WHERE scopeStaff.BranchId = @BranchId
        UNION ALL
        SELECT scopeCc.CustomerId FROM CarCustomer scopeCc WITH (READUNCOMMITTED)
        JOIN [User] scopeCcUser WITH (READUNCOMMITTED) ON scopeCcUser.Id = scopeCc.UpdatedBy
        JOIN Staff scopeCcStaff WITH (READUNCOMMITTED) ON scopeCcStaff.Id = scopeCcUser.StaffId
        WHERE scopeCc.CustomerId IS NOT NULL AND ISNULL(scopeCc.Status, 1) <> 0
          AND scopeCcStaff.BranchId = @BranchId
        UNION ALL
        SELECT scopeJob.CustomerId FROM PJCarPickUp scopeJob WITH (READUNCOMMITTED)
        WHERE scopeJob.CustomerId IS NOT NULL AND scopeJob.BranchId = @BranchId
        """;

    internal const string VehicleIds = """
        SELECT ownedCar.Id FROM Car ownedCar WITH (READUNCOMMITTED)
        JOIN [User] carUser WITH (READUNCOMMITTED) ON carUser.Id = ownedCar.UpdatedBy
        JOIN Staff carStaff WITH (READUNCOMMITTED) ON carStaff.Id = carUser.StaffId
        WHERE carStaff.BranchId = @BranchId
        UNION ALL
        SELECT vehicleCc.CarId FROM CarCustomer vehicleCc WITH (READUNCOMMITTED)
        JOIN [User] linkUser WITH (READUNCOMMITTED) ON linkUser.Id = vehicleCc.UpdatedBy
        JOIN Staff linkStaff WITH (READUNCOMMITTED) ON linkStaff.Id = linkUser.StaffId
        WHERE vehicleCc.CarId IS NOT NULL AND ISNULL(vehicleCc.Status, 1) <> 0
          AND linkStaff.BranchId = @BranchId
        UNION ALL
        SELECT vehicleJob.CarId FROM PJCarPickUp vehicleJob WITH (READUNCOMMITTED)
        WHERE vehicleJob.CarId IS NOT NULL AND vehicleJob.BranchId = @BranchId
        """;
}
