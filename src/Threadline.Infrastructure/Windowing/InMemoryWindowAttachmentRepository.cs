using System.Collections.Concurrent;
using Threadline.Core;

namespace Threadline.Infrastructure.Windowing;

public sealed class InMemoryWindowAttachmentRepository : IWindowAttachmentRepository
{
    private readonly ConcurrentDictionary<string, WindowAttachment> _attachments = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WindowActionRequest> _actions = new(StringComparer.OrdinalIgnoreCase);

    public Task<WindowAttachment> SaveAttachmentAsync(WindowAttachment attachment, CancellationToken cancellationToken = default)
    {
        if (attachment.Status == WindowAttachmentStatus.Attached)
        {
            foreach (var existing in _attachments.Values.Where(item => item.SessionId == attachment.SessionId && item.Status == WindowAttachmentStatus.Attached))
            {
                _attachments[existing.Id] = existing.Detach(attachment.AttachedAt);
            }
        }

        _attachments[attachment.Id] = attachment;
        return Task.FromResult(attachment);
    }

    public Task<WindowAttachment?> GetAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_attachments.TryGetValue(attachmentId, out var attachment) ? attachment : null);

    public Task<WindowAttachment?> GetActiveAttachmentAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_attachments.Values
            .Where(item => item.SessionId == sessionId && item.Status == WindowAttachmentStatus.Attached)
            .OrderByDescending(item => item.AttachedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<WindowAttachment>> ListAttachmentsAsync(string sessionId, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WindowAttachment>>(_attachments.Values
            .Where(item => item.SessionId == sessionId)
            .OrderByDescending(item => item.AttachedAt)
            .Take(take)
            .ToArray());

    public Task<WindowActionRequest> SaveActionAsync(WindowActionRequest action, CancellationToken cancellationToken = default)
    {
        _actions[action.Id] = action;
        return Task.FromResult(action);
    }

    public Task<WindowActionRequest?> GetActionAsync(string actionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_actions.TryGetValue(actionId, out var action) ? action : null);

    public Task<IReadOnlyList<WindowActionRequest>> ListActionsAsync(string sessionId, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WindowActionRequest>>(_actions.Values
            .Where(item => item.SessionId == sessionId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(take)
            .ToArray());
}
