#nullable enable
using Content.IntegrationTests.Pair;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WG.BulkCargo;

/// <summary>Builds delivery zones, floors and a funded buyer for the bulk crate order tests.</summary>
public static class BulkCargoTestWorld
{
    public const string Corner = "WGCargoZoneCorner";
    public const string Vendor = "VendingMachineTraderDepot";
    public const string Fabric = "TradeGoodFabric";
    public const string Ammo = "TradeGoodAmmo";
    public const int Funds = 500_000;

    /// <summary>Lays a rectangle of one tile, from the origin corner.</summary>
    public static void Floor(IEntityManager em, EntityUid grid, Tile tile, int width, int height)
    {
        var map = em.System<SharedMapSystem>();
        var gridComp = em.GetComponent<MapGridComponent>(grid);
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
                map.SetTile(grid, gridComp, new Vector2i(x, y), tile);
        }
    }

    /// <summary>Spawns a grid with a floor rectangle and moves it into place.</summary>
    public static EntityUid Grid(IEntityManager em, TestMapData map, Vector2 position, int width, int height)
    {
        var mapSystem = em.System<SharedMapSystem>();
        var grid = mapSystem.CreateGridEntity(map.MapId);
        Floor(em, grid.Owner, map.Tile.Tile, width, height);
        em.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, position);
        return grid.Owner;
    }

    /// <summary>Places one zone corner at the centre of a tile.</summary>
    public static EntityUid Marker(IEntityManager em, EntityUid grid, int x, int y)
    {
        return em.SpawnEntity(Corner, new EntityCoordinates(grid, x + 0.5f, y + 0.5f));
    }

    /// <summary>Creates a connected player's body with funds, since charging reads the player's saved character.</summary>
    public static EntityUid Payer(TestPair pair, MapId mapId)
    {
        var server = pair.Server;
        var em = server.EntMan;
        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var mind = server.System<SharedMindSystem>();
        mind.WipeMind(session.ContentData()?.Mind);

        var payer = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
        mind.TransferTo(mind.CreateMind(session.UserId).Owner, payer);
        em.EnsureComponent<BankAccountComponent>(payer);
        Assert.That(server.System<BankSystem>().TryBankDeposit(payer, Funds, tax: false), Is.True,
            "Could not fund the payer.");
        return payer;
    }
}
