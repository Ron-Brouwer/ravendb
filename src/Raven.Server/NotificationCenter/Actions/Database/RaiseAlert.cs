﻿using System;
using Raven.Server.NotificationCenter.Actions.Details;
using Raven.Server.NotificationCenter.Alerts;
using Sparrow.Json.Parsing;

namespace Raven.Server.NotificationCenter.Actions.Database
{
    public class RaiseAlert : DatabaseAction, IAlert
    {
        private RaiseAlert()
        {
        }

        public AlertSeverity Severity { get; set; }

        public DatabaseAlertType AlertType { get; set; }

        public string Key { get; set; }

        public string AlertId => AlertUtil.CreateId(AlertType, Key);

        public DateTime? DismissedUntil { get; set; }

        public override DynamicJsonValue ToJson()
        {
            var json = base.ToJson();

            json[nameof(AlertId)] = AlertId;
            json[nameof(Key)] = Key;
            json[nameof(Severity)] = Severity.ToString();
            json[nameof(DismissedUntil)] = DismissedUntil;
            json[nameof(AlertType)] = AlertType.ToString();

            return json;
        }

        public static RaiseAlert Create(string title, string msg, DatabaseAlertType type, AlertSeverity severity, string key = null, IActionDetails details = null)
        {
            return new RaiseAlert
            {
                Title = title,
                Message = msg,
                Type = ActionType.Alert,
                AlertType = type,
                Severity = severity,
                Key = key,
                Details = details
            };
        }
    }
}