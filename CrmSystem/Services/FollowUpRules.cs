using CrmSystem.Infrastructure;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace CrmSystem.Services;

/// <summary>
/// One place for the follow-up scheduling rules used by the activity form,
/// the follow-up form and the "mark as completed" form.
/// </summary>
public static class FollowUpRules
{
    /// <summary>Validates the "schedule next follow-up" section when it is ticked.</summary>
    public static void ValidateNext(NextFollowUpFields f, ModelStateDictionary modelState, SettingsSnapshot settings, IStringLocalizer l)
    {
        if (!f.ScheduleFollowUp) return;
        if (!f.FollowUpDate.HasValue)
            modelState.AddModelError(nameof(f.FollowUpDate), l["Validation.Required", l["Field.FollowUpDate"]]);
        if (!f.FollowUpTypeId.HasValue)
            modelState.AddModelError(nameof(f.FollowUpTypeId), l["Validation.Required", l["Field.FollowUpType"]]);
        if (string.IsNullOrWhiteSpace(f.FollowUpDescription))
            modelState.AddModelError(nameof(f.FollowUpDescription), l["Validation.Required", l["Field.FollowUpDescription"]]);
        if (f.FollowUpDate.HasValue)
            ValidateFuture(f.FollowUpDate.Value, f.FollowUpTime ?? new TimeOnly(9, 0), nameof(f.FollowUpDate), modelState, settings, l);
    }

    /// <summary>A follow-up must be scheduled in the future (local time of the system time zone).</summary>
    public static void ValidateFuture(DateOnly date, TimeOnly time, string key, ModelStateDictionary modelState, SettingsSnapshot settings, IStringLocalizer l)
    {
        if (date.ToDateTime(time) <= settings.NowLocal)
            modelState.AddModelError(key, l["FollowUps.MustBeFuture"]);
    }

    /// <summary>Builds a new follow-up from the "next follow-up" fields.</summary>
    public static FollowUp Build(NextFollowUpFields f, int clientId, string fallbackAssignee, bool canAssignOthers, SettingsSnapshot settings)
    {
        var followUp = new FollowUp
        {
            ClientId = clientId,
            ContactId = f.FollowUpContactId,
            FollowUpTypeId = f.FollowUpTypeId!.Value,
            Description = f.FollowUpDescription!.Trim(),
            AssignedToId = canAssignOthers && !string.IsNullOrWhiteSpace(f.FollowUpAssignedToId) ? f.FollowUpAssignedToId! : fallbackAssignee,
            Priority = f.FollowUpPriority,
            Status = FollowUpStatus.Pending,
            ReminderMinutesBefore = settings.FollowUpReminderMinutes,
            ReminderStatus = ReminderStatus.Scheduled
        };
        followUp.SetSchedule(f.FollowUpDate!.Value, f.FollowUpTime ?? new TimeOnly(9, 0), settings.TimeZoneId);
        return followUp;
    }

    /// <summary>
    /// When the reminder time is already in the past (follow-up created less than
    /// 24 h before it is due) the engine sends an immediate reminder: tell the user.
    /// </summary>
    public static string? LateReminderWarning(FollowUp f, IStringLocalizer l) =>
        f.CalculateReminderAtUtc() <= DateTime.UtcNow ? l["FollowUps.LateReminderWarning"].Value : null;

    /// <summary>Default values for a new "next follow-up" section.</summary>
    public static void SetDefaults(NextFollowUpFields f, SettingsSnapshot settings, string assigneeId, string? assigneeName)
    {
        f.FollowUpDate ??= settings.TodayLocal.AddDays(1);
        f.FollowUpTime ??= new TimeOnly(9, 0);
        f.FollowUpAssignedToId ??= assigneeId;
        f.FollowUpAssignedToName ??= assigneeName;
    }
}
