// Distributed under terms and conditions of CC0 licence. See LICENSE_CC0.txt for details.
using UnityEngine;

namespace CatenaryDC.catenary;

public partial class overhead_equipment
{
    // Retain the managed delegate target so unsubscribe does not depend on a
    // GameObject/Component surviving Unity's scene teardown.
    private OCS_ticker? _player_tracker;

    private void start_player_tracker()
    {
        _OCS_ticker = new GameObject("CatenaryDC.OCS.PlayerTracker", typeof(OCS_ticker));
        _player_tracker = _OCS_ticker.GetComponent<OCS_ticker>();
        _player_tracker.bind_owner(this);
        PlayerManager.PlayerTeleportStarted += _player_tracker.suspend_tracker;
        PlayerManager.PlayerTeleportFinished += _player_tracker.resume_tracker;
        PlayerManager.PlayerChanged += restart_tracker;
        _player_tracker.resume_tracker();
    }

    private void restart_tracker()
    {
        if (_ready && _player_tracker != null) _player_tracker.resume_tracker();
    }

    private void stop_player_tracker()
    {
        var tracker = _player_tracker;
        _player_tracker = null;
        PlayerManager.PlayerChanged -= restart_tracker;
        // Deliberately a managed-reference check: removing delegates must also
        // work when the native component is already destroyed.
        if (!ReferenceEquals(tracker, null))
        {
            PlayerManager.PlayerTeleportStarted -= tracker.suspend_tracker;
            PlayerManager.PlayerTeleportFinished -= tracker.resume_tracker;
            tracker.stop_tracker();
        }
        var host = _OCS_ticker;
        _OCS_ticker = null;
        if (host != null) GameObject.Destroy(host);
    }
}
