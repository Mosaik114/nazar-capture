//! Command-line parsing.
//!
//! | Argument          | Effect                                                  |
//! |-------------------|---------------------------------------------------------|
//! | *(none)*          | open the settings window                                |
//! | `recording`       | open the settings window, focused on the recording key  |
//! | `--background`    | start silently in the notification area                 |
//! | `--screenshot`    | take a screenshot                                       |
//! | `--recording`     | start or stop a recording                               |
//! | `--quit`          | quit the running instance                               |
//! | `--probe`         | exit with 0 only if a healthy instance is running       |
//! | `--self-test`     | run the built-in checks and exit                        |
//! | `--install [dir]` | install or update from this executable's folder         |
//!
//! When several actions are given, the last one wins; `--self-test` and `--install`
//! win over all others. The argument after `--install` is the target folder unless it
//! starts with `--`. Unknown arguments are ignored.
//! A separate settings file for tests or previews is chosen through the environment
//! (see [`crate::isolation`]), never on the command line.

use std::ffi::OsString;
use std::path::PathBuf;

use crate::ipc;

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Command {
    SelfTest,
    /// Install or update from the folder of this executable, into the given folder
    /// or the default one.
    Install(Option<PathBuf>),
    /// Run normally, or forward the action to the running instance.
    Run(Action),
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Action {
    /// Start without showing anything.
    Background,
    #[default]
    ShowSettings,
    /// Show the settings window with the recording hotkey focused.
    ShowRecordingSettings,
    TakeScreenshot,
    ToggleRecording,
    Quit,
    /// Check that a healthy instance is running.
    Probe,
}

impl Action {
    /// The message that forwards this action to an already running instance.
    pub const fn message(self) -> Option<u32> {
        match self {
            Self::Background => None,
            Self::ShowSettings => Some(ipc::SHOW_SETTINGS),
            Self::ShowRecordingSettings => Some(ipc::SHOW_RECORDING_SETTINGS),
            Self::TakeScreenshot => Some(ipc::TAKE_SCREENSHOT),
            Self::ToggleRecording => Some(ipc::TOGGLE_RECORDING),
            Self::Quit => Some(ipc::QUIT),
            Self::Probe => Some(ipc::PROBE),
        }
    }

    fn from_arg(arg: &OsString) -> Option<Self> {
        Some(match arg.to_str()? {
            "--background" => Self::Background,
            "--screenshot" => Self::TakeScreenshot,
            "--recording" => Self::ToggleRecording,
            "--quit" => Self::Quit,
            "--probe" => Self::Probe,
            "recording" => Self::ShowRecordingSettings,
            _ => return None,
        })
    }
}

/// Parses the arguments that follow the program name.
pub fn parse(args: impl IntoIterator<Item = OsString>) -> Command {
    let mut action = Action::default();
    let mut install = None;
    let mut args = args.into_iter().peekable();
    while let Some(arg) = args.next() {
        if arg == "--self-test" {
            return Command::SelfTest;
        }
        if arg == "--install" {
            let is_folder = |next: &OsString| !next.to_string_lossy().starts_with("--");
            install = Some(args.next_if(is_folder).map(PathBuf::from));
        } else if let Some(parsed) = Action::from_arg(&arg) {
            action = parsed;
        }
    }
    install.map_or(Command::Run(action), Command::Install)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn run(args: &[&str]) -> Command {
        parse(args.iter().map(OsString::from))
    }

    fn action(args: &[&str]) -> Action {
        match run(args) {
            Command::Run(action) => action,
            other => panic!("unexpected {other:?} for {args:?}"),
        }
    }

    #[test]
    fn no_arguments_show_settings() {
        assert_eq!(run(&[]), Command::Run(Action::ShowSettings));
    }

    #[test]
    fn each_action_flag() {
        assert_eq!(action(&["--background"]), Action::Background);
        assert_eq!(action(&["--screenshot"]), Action::TakeScreenshot);
        assert_eq!(action(&["--recording"]), Action::ToggleRecording);
        assert_eq!(action(&["--quit"]), Action::Quit);
        assert_eq!(action(&["--probe"]), Action::Probe);
        assert_eq!(action(&["recording"]), Action::ShowRecordingSettings);
    }

    #[test]
    fn last_action_wins_and_unknown_args_are_ignored() {
        assert_eq!(action(&["--screenshot", "--recording"]), Action::ToggleRecording);
        assert_eq!(action(&["--probe", "--whatever", "--background"]), Action::Background);
        assert_eq!(action(&["--Background", "RECORDING"]), Action::ShowSettings);
        assert_eq!(action(&["--preview", "a.ini"]), Action::ShowSettings, "no longer an option");
    }

    #[test]
    fn self_test_wins_anywhere() {
        assert_eq!(run(&["--self-test"]), Command::SelfTest);
        assert_eq!(run(&["--quit", "--self-test"]), Command::SelfTest);
        assert_eq!(run(&["--install", "--self-test"]), Command::SelfTest);
    }

    #[test]
    fn install_with_and_without_folder() {
        assert_eq!(run(&["--install"]), Command::Install(None));
        assert_eq!(
            run(&["--install", r"D:\Apps\Nazar"]),
            Command::Install(Some(PathBuf::from(r"D:\Apps\Nazar")))
        );
        assert_eq!(run(&["--install", "--background"]), Command::Install(None), "not a folder");
        assert_eq!(run(&["--screenshot", "--install"]), Command::Install(None), "install wins");
        assert_eq!(
            run(&["--install", "ziel", "--quit"]),
            Command::Install(Some(PathBuf::from("ziel")))
        );
    }

    #[test]
    fn forwarded_messages() {
        assert_eq!(Action::Background.message(), None);
        assert_eq!(Action::ShowSettings.message(), Some(ipc::SHOW_SETTINGS));
        assert_eq!(Action::ShowRecordingSettings.message(), Some(ipc::SHOW_RECORDING_SETTINGS));
        assert_eq!(Action::TakeScreenshot.message(), Some(ipc::TAKE_SCREENSHOT));
        assert_eq!(Action::ToggleRecording.message(), Some(ipc::TOGGLE_RECORDING));
        assert_eq!(Action::Quit.message(), Some(ipc::QUIT));
        assert_eq!(Action::Probe.message(), Some(ipc::PROBE));
    }
}
