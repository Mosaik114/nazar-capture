//! Parsing of user-entered hotkeys such as `Shift+Alt+4`, `Strg + Leertaste` (German
//! for Ctrl + Space) or `Druck` (Print), and their German key-cap labels.
//!
//! The format is a `+`-separated list of tokens. Spaces are ignored everywhere and
//! tokens are matched case-insensitively. A hotkey needs exactly one key and Ctrl, Alt
//! or Win (Shift alone is not enough: Shift+A would swallow every capital A). Print and
//! Pause type nothing, so they may stand alone or with Shift alone.
//!
//! * modifiers: `Shift`/`Umschalt`, `Alt`, `Ctrl`/`Control`/`Strg`, `Win`/`Windows`
//!   (German keyboard labels are accepted next to the English ones)
//! * keys: a single letter `A`–`Z` or digit `0`–`9`, `Space`/`Leertaste`, `F1`–`F24`,
//!   `Print`/`Druck`/`PrintScreen`/`PrtSc`/`Snapshot`, `Pause`/`Untbr`,
//!   `Insert`/`Einfg`/`Ins`, `Num0`–`Num9`, `NumMultiply`, `NumAdd`, `NumSubtract`,
//!   `NumDecimal`, `NumDivide`
//!
//! The engine's settings window parses the same texts (`engine/Core/HotkeyText.cs`);
//! both sides must accept and reject exactly the same strings.

use std::fmt;
use std::ops::{BitOr, BitOrAssign};
use std::str::FromStr;

use windows_sys::Win32::UI::Input::KeyboardAndMouse::{
    MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN, VK_ADD, VK_DECIMAL, VK_DIVIDE, VK_F1, VK_INSERT,
    VK_MULTIPLY, VK_NUMPAD0, VK_PAUSE, VK_SNAPSHOT, VK_SPACE, VK_SUBTRACT,
};

/// Tokens longer than this are rejected outright.
const MAX_TOKEN_LEN: usize = 24;
/// Highest supported function key (`F24`).
const MAX_FUNCTION_KEY: u16 = 24;

/// Named keys beyond letters, digits, function keys and `Num0`–`Num9`: every spelling
/// (matched case-insensitively), the virtual-key code and the German key-cap label.
const NAMED_KEYS: &[(&[&str], u16, &str)] = &[
    (&["SPACE", "LEERTASTE"], VK_SPACE, "Leertaste"),
    (&["PRINT", "DRUCK", "PRINTSCREEN", "PRTSC", "SNAPSHOT"], VK_SNAPSHOT, "Druck"),
    (&["PAUSE", "UNTBR"], VK_PAUSE, "Pause"),
    (&["INSERT", "EINFG", "INS"], VK_INSERT, "Einfg"),
    (&["NUMMULTIPLY"], VK_MULTIPLY, "Num *"),
    (&["NUMADD"], VK_ADD, "Num +"),
    (&["NUMSUBTRACT"], VK_SUBTRACT, "Num -"),
    (&["NUMDECIMAL"], VK_DECIMAL, "Num ,"),
    (&["NUMDIVIDE"], VK_DIVIDE, "Num /"),
];
/// A set of modifier keys, using the `MOD_*` bit values `RegisterHotKey` expects.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub struct Modifiers(u32);

impl Modifiers {
    pub const NONE: Self = Self(0);
    pub const ALT: Self = Self(MOD_ALT);
    pub const CONTROL: Self = Self(MOD_CONTROL);
    pub const SHIFT: Self = Self(MOD_SHIFT);
    /// The Windows logo key.
    pub const WIN: Self = Self(MOD_WIN);

    pub const fn bits(self) -> u32 {
        self.0
    }

    /// Whether every modifier in `other` is also set in `self`.
    pub const fn contains(self, other: Self) -> bool {
        self.0 & other.0 == other.0
    }

    pub const fn is_empty(self) -> bool {
        self.0 == 0
    }

    /// Recognises a modifier token (German and English spellings).
    fn from_token(token: &str) -> Option<Self> {
        let is = |word: &str| token.eq_ignore_ascii_case(word);
        if is("SHIFT") || is("UMSCHALT") {
            Some(Self::SHIFT)
        } else if is("ALT") {
            Some(Self::ALT)
        } else if is("CTRL") || is("CONTROL") || is("STRG") {
            Some(Self::CONTROL)
        } else if is("WIN") || is("WINDOWS") {
            Some(Self::WIN)
        } else {
            None
        }
    }
}

impl BitOr for Modifiers {
    type Output = Self;

    fn bitor(self, rhs: Self) -> Self {
        Self(self.0 | rhs.0)
    }
}

impl BitOrAssign for Modifiers {
    fn bitor_assign(&mut self, rhs: Self) {
        self.0 |= rhs.0;
    }
}

/// A global hotkey: modifiers plus one virtual-key code.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub struct Hotkey {
    pub modifiers: Modifiers,
    /// The virtual-key code (`VK_*`).
    pub key: u16,
}

/// The names on the key caps that differ between languages (the interface language
/// decides, as in the engine's key caps).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct KeyCaps {
    pub ctrl: &'static str,
    pub shift: &'static str,
    pub space: &'static str,
    pub print: &'static str,
    pub pause: &'static str,
    pub insert: &'static str,
    /// The decimal key of the number pad (`Num ,` on German keyboards).
    pub num_decimal: &'static str,
}

impl KeyCaps {
    /// German key caps: `Strg`, `Umschalt`, `Leertaste`, `Druck`, `Einfg`.
    pub const GERMAN: Self = Self {
        ctrl: "Strg",
        shift: "Umschalt",
        space: "Leertaste",
        print: "Druck",
        pause: "Pause",
        insert: "Einfg",
        num_decimal: "Num ,",
    };
}

impl Hotkey {
    /// The hotkey as printed on German key caps, modifiers first in the order Strg,
    /// Umschalt, Alt, Win: `Umschalt+Alt+4`, `Strg+Leertaste`, `Alt+Num 5`, `Druck`.
    #[cfg(test)]
    pub fn label(self) -> String {
        self.label_with(&KeyCaps::GERMAN)
    }

    /// The hotkey with the given key-cap names, modifiers first in the order Ctrl,
    /// Shift, Alt, Win.
    pub fn label_with(self, caps: &KeyCaps) -> String {
        let modifiers = [
            (Modifiers::CONTROL, caps.ctrl),
            (Modifiers::SHIFT, caps.shift),
            (Modifiers::ALT, "Alt"),
            (Modifiers::WIN, "Win"),
        ];
        let mut parts: Vec<String> = modifiers
            .iter()
            .filter(|(modifier, _)| self.modifiers.contains(*modifier))
            .map(|(_, label)| (*label).to_owned())
            .collect();
        parts.push(key_label(self.key, caps));
        parts.join("+")
    }
}

/// Whether `key` may be a hotkey without Ctrl, Alt or Win: Print and Pause type nothing.
const fn stands_alone(key: u16) -> bool {
    key == VK_SNAPSHOT || key == VK_PAUSE
}

/// The key-cap label of a virtual key that [`parse_key`] produces.
fn key_label(key: u16, caps: &KeyCaps) -> String {
    let own = match key {
        VK_SPACE => Some(caps.space),
        VK_SNAPSHOT => Some(caps.print),
        VK_PAUSE => Some(caps.pause),
        VK_INSERT => Some(caps.insert),
        VK_DECIMAL => Some(caps.num_decimal),
        _ => None,
    };
    if let Some(label) = own {
        return label.to_owned();
    }
    if let Some((_, _, label)) = NAMED_KEYS.iter().find(|(_, code, _)| *code == key) {
        return (*label).to_owned();
    }
    if (VK_NUMPAD0..VK_NUMPAD0 + 10).contains(&key) {
        return format!("Num {}", key - VK_NUMPAD0);
    }
    if (VK_F1..VK_F1 + MAX_FUNCTION_KEY).contains(&key) {
        return format!("F{}", key - VK_F1 + 1);
    }
    // Letters and digits: the virtual-key code is the ASCII character.
    u8::try_from(key).map_or_else(|_| format!("#{key}"), |byte| char::from(byte).to_string())
}

/// Why a hotkey string was rejected.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ParseHotkeyError {
    /// The text is empty or contains an empty token, as in `Alt++4`.
    EmptyToken,
    /// A token is longer than any valid key name.
    TokenTooLong,
    DuplicateModifier,
    /// More than one non-modifier key was given.
    MultipleKeys,
    /// A token is neither a modifier nor a supported key.
    UnknownKey,
    /// Only modifiers were given.
    MissingKey,
    /// A key other than Print or Pause was given without any modifier.
    MissingModifier,
    /// Shift is the only modifier; Ctrl, Alt or Win is required (except for Print and
    /// Pause).
    ShiftOnly,
}

impl fmt::Display for ParseHotkeyError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(match self {
            Self::EmptyToken => "empty key name",
            Self::TokenTooLong => "key name is too long",
            Self::DuplicateModifier => "modifier given twice",
            Self::MultipleKeys => "more than one key given",
            Self::UnknownKey => "unknown key",
            Self::MissingKey => "no key given",
            Self::MissingModifier => "no modifier given",
            Self::ShiftOnly => "Shift alone is not enough; add Ctrl, Alt or Win",
        })
    }
}

impl std::error::Error for ParseHotkeyError {}

impl FromStr for Hotkey {
    type Err = ParseHotkeyError;

    fn from_str(text: &str) -> Result<Self, Self::Err> {
        let compact: String = text.chars().filter(|&c| c != ' ').collect();
        let mut modifiers = Modifiers::NONE;
        let mut key = None;

        for token in compact.split('+') {
            if token.is_empty() {
                return Err(ParseHotkeyError::EmptyToken);
            }
            if token.chars().count() > MAX_TOKEN_LEN {
                return Err(ParseHotkeyError::TokenTooLong);
            }
            if let Some(modifier) = Modifiers::from_token(token) {
                if modifiers.contains(modifier) {
                    return Err(ParseHotkeyError::DuplicateModifier);
                }
                modifiers |= modifier;
            } else if key.is_some() {
                return Err(ParseHotkeyError::MultipleKeys);
            } else {
                key = Some(parse_key(token).ok_or(ParseHotkeyError::UnknownKey)?);
            }
        }

        let key = key.ok_or(ParseHotkeyError::MissingKey)?;
        if !stands_alone(key) {
            if modifiers.is_empty() {
                return Err(ParseHotkeyError::MissingModifier);
            }
            if modifiers == Modifiers::SHIFT {
                return Err(ParseHotkeyError::ShiftOnly);
            }
        }
        Ok(Self { modifiers, key })
    }
}

fn parse_key(token: &str) -> Option<u16> {
    let mut chars = token.chars();
    if let (Some(c), None) = (chars.next(), chars.next()) {
        // Letters and digits share their virtual-key codes with uppercase ASCII.
        return u8::try_from(c)
            .ok()
            .filter(u8::is_ascii_alphanumeric)
            .map(|b| u16::from(b.to_ascii_uppercase()));
    }
    let named = NAMED_KEYS
        .iter()
        .find(|(spellings, _, _)| spellings.iter().any(|name| token.eq_ignore_ascii_case(name)));
    if let Some((_, key, _)) = named {
        return Some(*key);
    }
    if let Some(digit) = numpad_digit(token) {
        return Some(VK_NUMPAD0 + digit);
    }
    let number = token.strip_prefix(['F', 'f'])?;
    if number.is_empty() || !number.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    // Leading zeros are accepted (`F07`); overflow means "far too large".
    let n: u16 = number.parse().ok()?;
    (1..=MAX_FUNCTION_KEY).contains(&n).then(|| VK_F1 + n - 1)
}

/// The digit of `Num0`–`Num9` (`Num` in any case).
fn numpad_digit(token: &str) -> Option<u16> {
    let &[n, u, m, digit] = token.as_bytes() else {
        return None;
    };
    let is_num = [n, u, m].eq_ignore_ascii_case(b"NUM");
    (is_num && digit.is_ascii_digit()).then(|| u16::from(digit - b'0'))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::self_test::HOTKEY_CASES;

    fn key(modifiers: u32, key: u16) -> Hotkey {
        Hotkey { modifiers: Modifiers(modifiers), key }
    }

    #[test]
    fn self_test_cases_hold() {
        for &(text, expected) in HOTKEY_CASES {
            let parsed = text.parse::<Hotkey>().ok().map(|k| (k.modifiers.bits(), k.key));
            assert_eq!(parsed, expected, "{text:?}");
        }
    }

    #[test]
    fn letters_and_digits_are_case_insensitive() {
        assert_eq!("alt+a".parse(), Ok(key(MOD_ALT, u16::from(b'A'))));
        assert_eq!("ALT+Z".parse(), Ok(key(MOD_ALT, u16::from(b'Z'))));
        assert_eq!("sHiFt+aLt+0".parse(), Ok(key(MOD_SHIFT | MOD_ALT, u16::from(b'0'))));
        assert_eq!("Strg+9".parse(), Ok(key(MOD_CONTROL, u16::from(b'9'))));
    }

    #[test]
    fn all_modifier_spellings() {
        for text in ["Shift+Alt+X", "Umschalt+Alt+X", "SHIFT+alt+x", "umschalt+ALT+x"] {
            assert_eq!(
                text.parse::<Hotkey>().map(|k| k.modifiers),
                Ok(Modifiers::SHIFT | Modifiers::ALT)
            );
        }
        for text in ["Ctrl+X", "Control+X", "Strg+X", "STRG+x"] {
            assert_eq!(text.parse::<Hotkey>().map(|k| k.modifiers), Ok(Modifiers::CONTROL));
        }
        let all = "Strg+Alt+Umschalt+K".parse::<Hotkey>().unwrap();
        assert_eq!(all.modifiers, Modifiers::ALT | Modifiers::CONTROL | Modifiers::SHIFT);
    }

    #[test]
    fn modifier_order_does_not_matter() {
        let a: Hotkey = "Shift+Alt+4".parse().unwrap();
        let b: Hotkey = "Alt+Shift+4".parse().unwrap();
        let c: Hotkey = "4+Alt+Shift".parse().unwrap();
        assert_eq!(a, b);
        assert_eq!(a, c);
    }

    #[test]
    fn space_key_spellings() {
        assert_eq!("Alt+Space".parse(), Ok(key(MOD_ALT, VK_SPACE)));
        assert_eq!("Alt+Leertaste".parse(), Ok(key(MOD_ALT, VK_SPACE)));
        assert_eq!("alt+leertaste".parse(), Ok(key(MOD_ALT, VK_SPACE)));
    }

    #[test]
    fn function_keys() {
        assert_eq!("Alt+F1".parse(), Ok(key(MOD_ALT, VK_F1)));
        assert_eq!("Alt+f12".parse(), Ok(key(MOD_ALT, VK_F1 + 11)));
        assert_eq!("Alt+F07".parse(), Ok(key(MOD_ALT, VK_F1 + 6)));
        assert_eq!("Alt+F".parse(), Ok(key(MOD_ALT, u16::from(b'F'))));
        for text in ["Alt+F0", "Alt+F25", "Alt+F1x", "Alt+Fx", "Alt+F-1", "Alt+F99999999999"] {
            assert_eq!(text.parse::<Hotkey>(), Err(ParseHotkeyError::UnknownKey), "{text:?}");
        }
    }

    #[test]
    fn spaces_are_ignored_everywhere() {
        assert_eq!(" Sh ift + A lt + 4 ".parse(), Ok(key(MOD_SHIFT | MOD_ALT, u16::from(b'4'))));
        assert_eq!("Ctrl + Leer taste".parse(), Ok(key(MOD_CONTROL, VK_SPACE)));
    }

    #[test]
    fn tabs_are_not_whitespace() {
        assert_eq!("Alt+\t4".parse::<Hotkey>(), Err(ParseHotkeyError::UnknownKey));
    }

    #[test]
    fn structural_errors() {
        use ParseHotkeyError as E;
        let cases = [
            ("", E::EmptyToken),
            ("   ", E::EmptyToken),
            ("+Alt+4", E::EmptyToken),
            ("Alt+4+", E::EmptyToken),
            ("Alt++4", E::EmptyToken),
            ("Alt+Alt+4", E::DuplicateModifier),
            ("Strg+Ctrl+4", E::DuplicateModifier),
            ("Alt+4+5", E::MultipleKeys),
            ("Alt+Space+F1", E::MultipleKeys),
            ("Alt+Shift", E::MissingKey),
            ("Alt", E::MissingKey),
            ("A", E::MissingModifier),
            ("Shift+A", E::ShiftOnly),
            ("Umschalt+F5", E::ShiftOnly),
            ("Shift+Space", E::ShiftOnly),
            ("Space", E::MissingModifier),
            ("Alt+Ä", E::UnknownKey),
            ("Alt+Enter", E::UnknownKey),
            ("Alt+-", E::UnknownKey),
            ("Win", E::MissingKey),
            ("Win+Windows+4", E::DuplicateModifier),
            ("Shift+Einfg", E::ShiftOnly),
            ("Num5", E::MissingModifier),
            ("Alt+Num", E::UnknownKey),
            ("Alt+Num10", E::UnknownKey),
            ("Alt+NumEnter", E::UnknownKey),
            ("Alt+Druck+Pause", E::MultipleKeys),
            ("Strg+Shift+Alt+Win+Print+4", E::MultipleKeys),
        ];
        for (text, error) in cases {
            assert_eq!(text.parse::<Hotkey>(), Err(error), "{text:?}");
        }
    }

    #[test]
    fn token_length_limit() {
        let longest = format!("Alt+F{}", "0".repeat(MAX_TOKEN_LEN - 2) + "1");
        assert_eq!(longest.parse(), Ok(key(MOD_ALT, VK_F1)));
        let too_long = format!("Alt+F{}", "0".repeat(MAX_TOKEN_LEN - 1) + "1");
        assert_eq!(too_long.parse::<Hotkey>(), Err(ParseHotkeyError::TokenTooLong));
    }

    #[test]
    fn modifier_helpers() {
        assert!(Modifiers::NONE.is_empty());
        let both = Modifiers::ALT | Modifiers::SHIFT;
        assert!(both.contains(Modifiers::ALT));
        assert!(!both.contains(Modifiers::CONTROL));
        assert_eq!(both.bits(), 5);
    }

    #[test]
    fn win_modifier() {
        for text in ["Win+A", "Windows+A", "win + a", "WINDOWS+a"] {
            assert_eq!(text.parse(), Ok(key(MOD_WIN, u16::from(b'A'))), "{text:?}");
        }
        assert_eq!("Shift+Win+4".parse(), Ok(key(MOD_SHIFT | MOD_WIN, u16::from(b'4'))));
        let all = "Strg+Umschalt+Alt+Win+F5".parse::<Hotkey>().unwrap();
        assert_eq!(all.modifiers.bits(), MOD_CONTROL | MOD_SHIFT | MOD_ALT | MOD_WIN);
    }

    #[test]
    fn print_and_pause_may_stand_alone() {
        for text in ["Print", "Druck", "PrintScreen", "prtsc", "SNAPSHOT"] {
            assert_eq!(text.parse(), Ok(key(0, VK_SNAPSHOT)), "{text:?}");
        }
        for text in ["Pause", "untbr"] {
            assert_eq!(text.parse(), Ok(key(0, VK_PAUSE)), "{text:?}");
        }
        assert_eq!("Shift+Druck".parse(), Ok(key(MOD_SHIFT, VK_SNAPSHOT)), "Shift alone is fine");
        assert_eq!("Umschalt+Pause".parse(), Ok(key(MOD_SHIFT, VK_PAUSE)));
        assert_eq!("Strg+Alt+Druck".parse(), Ok(key(MOD_CONTROL | MOD_ALT, VK_SNAPSHOT)));
    }

    #[test]
    fn insert_and_numpad_keys() {
        for text in ["Alt+Insert", "Alt+Einfg", "Alt+ins"] {
            assert_eq!(text.parse(), Ok(key(MOD_ALT, VK_INSERT)), "{text:?}");
        }
        for digit in 0..10u16 {
            let text = format!("Strg+Num{digit}");
            assert_eq!(text.parse(), Ok(key(MOD_CONTROL, VK_NUMPAD0 + digit)), "{text:?}");
        }
        assert_eq!("Alt+num 7".parse(), Ok(key(MOD_ALT, VK_NUMPAD0 + 7)), "spaces ignored");
        let named = [
            ("NumMultiply", VK_MULTIPLY),
            ("NumAdd", VK_ADD),
            ("numsubtract", VK_SUBTRACT),
            ("NUMDECIMAL", VK_DECIMAL),
            ("NumDivide", VK_DIVIDE),
        ];
        for (name, code) in named {
            assert_eq!(format!("Alt+{name}").parse(), Ok(key(MOD_ALT, code)), "{name}");
        }
    }

    #[test]
    fn labels_use_german_key_caps() {
        let label = |text: &str| text.parse::<Hotkey>().unwrap().label();
        assert_eq!(label("Shift+Alt+4"), "Umschalt+Alt+4");
        assert_eq!(label("alt+shift+x"), "Umschalt+Alt+X", "canonical order, upper case");
        assert_eq!(label("Win+Ctrl+Space"), "Strg+Win+Leertaste");
        assert_eq!(label("PrintScreen"), "Druck");
        assert_eq!(label("Shift+Untbr"), "Umschalt+Pause");
        assert_eq!(label("Alt+Ins"), "Alt+Einfg");
        assert_eq!(label("Strg+Num5"), "Strg+Num 5");
        assert_eq!(label("Alt+NumMultiply"), "Alt+Num *");
        assert_eq!(label("Alt+NumAdd"), "Alt+Num +");
        assert_eq!(label("Alt+NumSubtract"), "Alt+Num -");
        assert_eq!(label("Alt+NumDecimal"), "Alt+Num ,");
        assert_eq!(label("Alt+NumDivide"), "Alt+Num /");
        assert_eq!(label("Strg+Shift+F12"), "Strg+Umschalt+F12");
        assert_eq!(label("Alt+F07"), "Alt+F7");
    }

    #[test]
    fn errors_have_messages() {
        assert_eq!(ParseHotkeyError::MissingKey.to_string(), "no key given");
    }
}
