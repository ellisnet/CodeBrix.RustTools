use chrono::{Datelike, Local};

fn format_datetime<Tz>(dt: chrono::DateTime<Tz>) -> String
where
    Tz: chrono::TimeZone,
    Tz::Offset: std::fmt::Display,
{
    let day_name = dt.format("%A");
    let day = dt.day();
    let month = dt.format("%B");
    let time = dt.format("%I:%M%P");

    let ordinal = match day {
        1 | 21 | 31 => "st",
        2 | 22 => "nd",
        3 | 23 => "rd",
        _ => "th",
    };

    format!("{} of the {}{} of {}, at {}", day_name, day, ordinal, month, time)
}

pub fn get_formatted_datetime() -> String {
    let now = Local::now();
    format_datetime(now)
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::{FixedOffset, TimeZone};

    #[test]
    fn formats_datetime_with_ordinal_and_time() {
        let tz = FixedOffset::west_opt(8 * 3600).unwrap();
        let dt = tz.with_ymd_and_hms(2026, 2, 1, 9, 5, 0).unwrap();
        let formatted = format_datetime(dt);

        assert!(formatted.contains("1st of February"));
        assert!(formatted.ends_with("at 09:05am"));
    }

    #[test]
    fn formats_ordinal_for_22nd() {
        let tz = FixedOffset::west_opt(8 * 3600).unwrap();
        let dt = tz.with_ymd_and_hms(2026, 2, 22, 13, 0, 0).unwrap();
        let formatted = format_datetime(dt);

        assert!(formatted.contains("22nd of February"));
        assert!(formatted.ends_with("at 01:00pm"));
    }
}
