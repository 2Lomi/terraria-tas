using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using TerrariaTas.Core;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Damage rolls of the held weapon: for each offset k of a stream, the crit and damage of a hit whose first roll is k
/// draws after the current frame boundary. The damage roll is the game's own <c>Main.DamageVar</c> run on copies of the
/// stream; the crit rolls and the defense step are a few lines inside the game's hit code, mirrored here.
/// </summary>
internal sealed class DamageForecaster
{
    public const string SwingStream = "UpdatePlayers";
    public const string ProjectileStream = "UpdateProjectiles";

    private const int MaxRows = 20_000;

    // Crit rolls, the damage roll and at most two more for luck.
    private const long MaxDrawsPerHit = 16;

    private static readonly AccessTools.FieldRef<Player, float> RapidAttackBonus = AccessTools.FieldRefAccess<Player, float>(AccessTools.Field(typeof(Player), "rapidAttackBonus"));

    // Flails swinging or thrown, rockets on their main target, mines lying still... It only reads the projectile.
    private static readonly Func<Projectile, float> SpecificDamageMultiplier =
        AccessTools.MethodDelegate<Func<Projectile, float>>(AccessTools.Method(typeof(Projectile), "Damage_GetProjectileSpecificDamageMultiplier"));

    private readonly RngTracker _rng;

    public DamageForecaster(RngTracker rng)
    {
        _rng = rng;
    }

    /// <summary>Call at a paused frame boundary inside a world.</summary>
    public DamageForecast Forecast(DamageRequest request, int frame)
    {
        var player = Main.LocalPlayer;
        var item = player.HeldItem;
        var forecast = new DamageForecast { RequestId = request.RequestId, Frame = frame, Luck = player.luck, TargetSlot = -1 };
        if (item == null || item.IsAir || item.damage <= 0)
        {
            forecast.Error = "Hold a weapon: the held item does no damage.";
            return forecast;
        }

        forecast.ItemId = item.type;
        forecast.ItemName = item.AffixName();
        var target = request.TargetSlot >= 0 && request.TargetSlot < Main.maxNPCs && Main.npc[request.TargetSlot] is { active: true } npc ? npc : null;
        if (target != null)
        {
            forecast.TargetSlot = target.whoAmI;
            forecast.TargetNetId = target.netID;
            forecast.TargetName = target.FullName;
            forecast.TargetLife = target.life;
            forecast.TargetDefense = target.defense;
        }

        var hits = new List<Hit>();
        if (!item.noMelee)
            hits.Add(Swing(player, item));
        if (Shot(player, item, target, out var noShot) is { } shot)
            hits.Add(shot);
        if (hits.Count == 0)
        {
            forecast.Error = noShot ?? $"{forecast.ItemName} neither hits with its swing nor shoots.";
            return forecast;
        }

        var rows = Math.Max(1, Math.Min(request.Rows, MaxRows));
        var shared = Main.rand;
        try
        {
            foreach (var hit in hits)
                forecast.Columns.Add(Column(hit.With(request), rows, player.luck, target));
        }
        finally
        {
            Main.rand = shared;
        }

        forecast.Notes = "The rolls of an ordinary hit. Banners, timed crits, Pyroclast, the Keybrand, monk armor, whip tags and other special "
                         + "effects add draws or change the damage: the hit log shows what real hits did.";
        return forecast;
    }

    /// <summary>What a swing of the held item starts from (<c>ItemCheck_MeleeHitNPCs</c> passes the weapon damage).</summary>
    [MirrorsGameMethod("Terraria.Player", "ProcessHitAgainstNPC")]
    private static Hit Swing(Player player, Item item) => new(
        HitKind.Swing,
        SwingStream,
        item.AffixName(),
        item.type,
        WithRapidAttackBonus(player, player.GetWeaponDamage(item), item.type),
        new[] { player.GetWeaponCrit(item) },
        player.GetArmorPenetration(item.melee));

    /// <summary>
    /// <c>Player.ApplyRapidAttackBonus</c> as the next swing sees it: <c>Player.ResetEffects</c> lowers the bonus at the
    /// start of the player's update, before the swing.
    /// </summary>
    [MirrorsGameMethod("Terraria.Player", "ApplyRapidAttackBonus")]
    [MirrorsGameMethod("Terraria.Player", "ResetEffects")]
    private static int WithRapidAttackBonus(Player player, int damage, int itemType)
    {
        if (!ItemID.Sets.RapidAttackBonusDamage[itemType])
            return damage;

        var bonus = RapidAttackBonus(player);
        if (bonus > 0f)
        {
            bonus -= 0.005f;
            if (bonus < 0f)
                bonus = 0f;
        }

        if (bonus > 0.5f)
            bonus = 0.5f;
        return (int)((float)damage * (1f + bonus));
    }

    /// <summary>
    /// What a projectile hit starts from: a projectile of the player already in flight (closest to the target, or to the
    /// player) carries its own damage, ammo included; otherwise what the weapon would shoot now. In single player the
    /// projectiles of town NPCs and of traps (falling stars too) also belong to the local player; they are not the
    /// player's own (<c>Projectile.OwnedBySomeone</c>), and the game rolls them no crit and no luck.
    /// </summary>
    /// <param name="noShot">Why there is no projectile hit, when it is worth saying.</param>
    private static Hit? Shot(Player player, Item item, NPC? target, out string? noShot)
    {
        noShot = null;
        var aim = target?.Center ?? player.Center;
        Projectile? flying = null;
        for (var i = 0; i < Main.maxProjectiles; i++)
        {
            var projectile = Main.projectile[i];
            if (projectile is { active: true, friendly: true, OwnedBySomeone: true } && projectile.owner == player.whoAmI && projectile.damage > 0
                && (flying == null || Vector2.DistanceSquared(projectile.Center, aim) < Vector2.DistanceSquared(flying.Center, aim)))
                flying = projectile;
        }

        if (flying != null)
        {
            return new Hit(
                HitKind.Projectile,
                ProjectileStream,
                $"{Lang.GetProjectileName(flying.type).Value} in flight (slot {flying.whoAmI})",
                flying.type,
                HitDamage(flying, player),
                ProjectileCritChances(player, flying.melee, flying.ranged, flying.magic, flying.bonusCritChance),
                player.GetArmorPenetration(flying.melee) + flying.armorPenetration);
        }

        return NextShot(player, item, out noShot);
    }

    /// <summary>
    /// The projectile the held weapon would shoot now, with the damage <c>Player.ItemCheck_Shoot</c> gives it: the
    /// weapon's, plus its ammo's (the ammo also picks the projectile). Shots of a few weapons change further.
    /// </summary>
    [MirrorsGameMethod("Terraria.Player", "ItemCheck_Shoot")]
    private static Hit? NextShot(Player player, Item item, out string? noShot)
    {
        noShot = null;
        var projectileType = item.shoot;
        var weaponDamage = player.GetWeaponDamage(item);
        var damage = weaponDamage;
        var ammo = "";
        if (item.useAmmo > 0)
        {
            if (!PickAmmo(player, item, ref projectileType, ref damage, out var ammoId))
            {
                noShot = $"{item.AffixName()} has no ammo to shoot.";
                return null;
            }

            if (ItemID.Sets.gunProj[item.type])
                damage = weaponDamage;
            ammo = " with " + Lang.GetItemNameValue(ammoId);
        }

        if (projectileType <= ProjectileID.None || !ContentSamples.ProjectilesByType.TryGetValue(projectileType, out var sample))
            return null;

        return new Hit(
            HitKind.Projectile,
            ProjectileStream,
            $"{Lang.GetProjectileName(sample.type).Value} shot by {item.AffixName()}{ammo}",
            sample.type,
            damage,
            ProjectileCritChances(player, sample.melee, sample.ranged, sample.magic, sample.bonusCritChance),
            player.GetArmorPenetration(sample.melee) + sample.armorPenetration);
    }

    /// <summary>
    /// The ammo the weapon would use now, with the projectile and damage it gives (the game's <c>Player.PickAmmo</c>, told
    /// not to consume). A few weapons and accessories draw ammo-saving chances there even then: they draw from a copy.
    /// </summary>
    private static bool PickAmmo(Player player, Item item, ref int projectileType, ref int damage, out int ammoId)
    {
        var speed = item.shootSpeed;
        var knockBack = item.knockBack;
        var canShoot = false;
        var shared = Main.rand;
        Main.rand = GameRng.Clone(shared);
        try
        {
            player.PickAmmo(item, ref projectileType, ref speed, ref canShoot, ref damage, ref knockBack, out ammoId, dontConsume: true);
        }
        finally
        {
            Main.rand = shared;
        }

        return canShoot;
    }

    /// <summary>The damage a projectile's hit rolls from: its damage with the changes of a few kinds of projectile.</summary>
    [MirrorsGameMethod("Terraria.Projectile", "Damage_PVE_Inner")]
    private static int HitDamage(Projectile projectile, Player owner)
    {
        var type = projectile.type;
        var damage = (int)((float)projectile.damage * SpecificDamageMultiplier(projectile));
        if (type > 0 && type < ProjectileID.Count && ProjectileID.Sets.StardustDragon[type])
            damage = (int)((float)damage * (1f + Utils.Clamp((projectile.scale - 1f) * 100f, 0f, 50f) * 0.23f));
        if (type > 0 && type < ProjectileID.Count && ProjectileID.Sets.StormTiger[type])
            damage = (int)((float)damage * (1f + (float)Math.Max(0, owner.ownedProjectileCounts[831] - 1) * 0.4f));
        if (type == 818)
            damage = (int)((float)damage * (1.5f + (float)Math.Max(0, owner.ownedProjectileCounts[831] - 1) * 0.4f));
        if (type == 963)
            damage = (int)((float)damage * (1f + (float)Math.Max(0, owner.ownedProjectileCounts[970] - 1) * (Main.hardMode ? 1.3f : 0.55f)));
        if (type == 301 && projectile.penetrate == 1000)
            damage = (int)((float)damage * 1.333f);
        if (projectile.OwnedBySomeone && type == 189 && owner.strongBees)
            damage += 5;
        return damage;
    }

    /// <summary>A projectile rolls a crit for each class it has (traps and NPC projectiles roll none).</summary>
    [MirrorsGameMethod("Terraria.Projectile", "Damage_PVE_Inner")]
    public static int[] ProjectileCritChances(Player owner, bool melee, bool ranged, bool magic, int bonusCritChance)
    {
        var bonus = Math.Max(0, bonusCritChance);
        var chances = new List<int>();
        if (melee)
            chances.Add(owner.meleeCrit + bonus);
        if (ranged)
            chances.Add(owner.rangedCrit + bonus);
        if (magic)
            chances.Add(owner.magicCrit + bonus);
        return chances.ToArray();
    }

    private DamageColumn Column(Hit hit, int rows, float luck, NPC? target)
    {
        var column = new DamageColumn
        {
            Kind = hit.Kind,
            Stream = hit.Stream,
            StartPosition = _rng.StartPositionOf(hit.Stream),
            Source = hit.Source,
            SourceId = hit.SourceId,
            Overridden = hit.Overridden,
            BaseDamage = hit.BaseDamage,
            CritChances = hit.CritChances,
        };
        var cursor = GameRng.CloneForNextFrame(hit.Stream);
        for (var offset = 0; offset < rows; offset++)
        {
            column.Rolls.Add(Roll(hit, GameRng.Clone(cursor), luck, target));
            cursor.Next();
        }

        return column;
    }

    private static DamageRoll Roll(Hit hit, UnifiedRandom generator, float luck, NPC? target)
    {
        var before = GameRng.ToModel(generator);
        Main.rand = generator; // hit code draws from Main.rand, which is the stream during its update phase
        var crit = RollCrit(hit.CritChances);
        var damage = Main.DamageVar(hit.BaseDamage, luck);
        return new DamageRoll
        {
            Crit = crit,
            Damage = damage,
            Dealt = target == null ? -1 : Dealt(target, damage + target.checkArmorPenetration(hit.ArmorPenetration, 0f), crit),
            Draws = (int)before.DrawsUntil(GameRng.ToModel(generator), MaxDrawsPerHit),
        };
    }

    /// <summary>Every crit roll is drawn, even after one succeeded.</summary>
    [MirrorsGameMethod("Terraria.Player", "ProcessHitAgainstNPC")]
    [MirrorsGameMethod("Terraria.Projectile", "Damage_PVE_Inner")]
    private static bool RollCrit(int[] chances)
    {
        var crit = false;
        foreach (var chance in chances)
        {
            if (Main.rand.Next(1, 101) <= chance)
                crit = true;
        }

        return crit;
    }

    /// <summary>The first lines of <c>NPC.StrikeNPC_Inner</c>: what the NPC loses from a hit of <paramref name="damage"/>.</summary>
    [MirrorsGameMethod("Terraria.NPC", "StrikeNPC_Inner")]
    private static int Dealt(NPC npc, int damage, bool crit)
    {
        var dealt = (int)Math.Max(1.0, Main.CalculateDamageNPCsTake(damage, npc.defense) * (crit ? 2 : 1));
        if (npc.RedHatSkeletronAdjustmentsEnabled())
            dealt = Math.Max(1, (int)(dealt * 0.7f));
        if (npc.takenDamageMultiplier > 1f)
            dealt = (int)(dealt * npc.takenDamageMultiplier);
        return Math.Max(1, dealt);
    }

    /// <summary>What one kind of hit starts from.</summary>
    private sealed class Hit
    {
        public Hit(HitKind kind, string stream, string source, int sourceId, int baseDamage, int[] critChances, int armorPenetration, bool overridden = false)
        {
            Kind = kind;
            Stream = stream;
            Source = source;
            SourceId = sourceId;
            BaseDamage = baseDamage;
            CritChances = critChances;
            ArmorPenetration = armorPenetration;
            Overridden = overridden;
        }

        public HitKind Kind { get; }

        public string Stream { get; }

        public string Source { get; }

        /// <summary>The item or projectile type.</summary>
        public int SourceId { get; }

        public int BaseDamage { get; }

        public int[] CritChances { get; }

        public int ArmorPenetration { get; }

        public bool Overridden { get; }

        /// <summary>The hit with Studio's replacements for the base damage and the crit chance, if any.</summary>
        public Hit With(DamageRequest request)
        {
            var critChances = request.CritChance < 0 ? CritChances : Array.ConvertAll(CritChances, _ => request.CritChance);
            var baseDamage = request.BaseDamage < 0 ? BaseDamage : request.BaseDamage;
            var source = request.BaseDamage < 0 ? Source : $"{Source}, base damage set to {baseDamage}";
            return new Hit(Kind, Stream, source, SourceId, baseDamage, critChances, ArmorPenetration, request.BaseDamage >= 0 || request.CritChance >= 0);
        }
    }
}
