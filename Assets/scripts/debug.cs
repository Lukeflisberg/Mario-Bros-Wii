using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class debug : MonoBehaviour
{
    public player player;
    public Text text;

    void FixedUpdate()
    {
        text.text = $"IsGrounded: {player.IsGrounded} \n" +
                    $"IsRunning: {player.IsRunning} \n" +
                    $"IsCrouching: {player.IsCrouching} \n" +
                    $"IsSliding: {player.IsSliding} \n" +
                    $"IsJumping: {player.IsJumping} \n" +
                    $"IsFalling: {player.IsFalling} \n" +
                    $"IsWallSliding: {player.IsWallSliding} \n" +
                    $"IsGroundSlamming: {player.IsGroundSlamming} \n" +
                    $"IsAirSpinning: {player.IsAirSpinning} \n" + 
                    $"IsSpinFreezing: {player.IsSpinFreezing} \n" + 
                    $"CanAirSpin: {player.CanAirSpin} \n" +
                    $"AirSpinCooldown: {player.AirSpinCooldownNormalized} \n" +
                    $"JumpComboCount: {player.JumpComboCount} \n" +
                    $"FacingDirection: {player.FacingDirection}";
    }
}
