using System.Collections.Concurrent;

namespace CsvProjectXml.Maui.Services;

/// <summary>In-memory repair sessions for the current desktop-app process.</summary>
public sealed class RepairSessionStore
{
    private readonly ConcurrentDictionary<Guid, PendingRepairSession> _sessions = new();

    public PendingRepairSession Add(PendingRepairSession session)
    {
        if (!_sessions.TryAdd(session.Id, session))
            throw new InvalidOperationException($"Repair session '{session.Id}' already exists.");
        return session;
    }

    public bool TryGet(Guid id, out PendingRepairSession session) => _sessions.TryGetValue(id, out session!);

    public void Update(PendingRepairSession session) => _sessions[session.Id] = session;

    public bool Remove(Guid id) => _sessions.TryRemove(id, out _);
}
