# Newfor CLI

A command-line tool for injecting teletext subtitles using the Newfor protocol over TCP/IP.

## Overview

Newfor CLI is a .NET console application that sends subtitles to teletext subtitle inserters using the Newfor protocol. It provides an interactive interface for testing subtitle transmission with various formatting options.

## Features

- **Newfor Protocol Support**: Full implementation of the Newfor protocol including CONNECT, BUILD, REVEAL, CLEAR, and DISCONNECT commands
- **Multiple Subtitle Lines**: Support for 1, 2, or 3 line subtitles
- **Text Colours**: Red, Green, Yellow, Blue, Magenta, Cyan, and White
- **Height Modes**: Single height (0x0C) and Double height (0x0D)
- **Boxing**: Optional teletext box background for improved readability
- **Vertical Positioning**: Top, Middle, or Lower screen positions
- **Hamming 8/4 Encoding**: Correct encoding for row numbers and control data
- **Odd Parity**: Proper parity bit handling for teletext transmission

## Requirements

- .NET 6.0 or later
- Network connectivity to a Newfor-compatible subtitle inserter

## Building

```bash
dotnet build
```

## Usage

### Command Line Arguments

```bash
NewforCli [--ip <address>] [--port <port>] [--page <page>]
```

| Argument | Description | Default |
|----------|-------------|---------|
| `--ip` | IP address of the subtitle inserter | 127.0.0.1 |
| `--port` | TCP port number | 1234 |
| `--page` | Teletext page number | 888 |

### Interactive Controls

Once connected, use the following keyboard controls:

#### Colour Selection
| Key | Colour |
|-----|--------|
| R | Red |
| G | Green |
| Y | Yellow |
| B | Blue |
| M | Magenta |
| A | Cyan |
| W | White |

#### Options
| Key | Action |
|-----|--------|
| X | Toggle box on/off |
| H | Toggle double height on/off |

#### Vertical Position
| Key | Position |
|-----|----------|
| T | Top (rows 2-4) |
| N | Middle (centred around row 12) |
| L | Lower (ending at row 22) |

#### Subtitle Transmission
| Key | Action |
|-----|--------|
| 1 | Send single line subtitle |
| 2 | Send two line subtitle |
| 3 | Send three line subtitle |

#### Other Actions
| Key | Action |
|-----|--------|
| C | Clear subtitle display |
| P | Change teletext page |
| Q | Quit (sends DISCONNECT) |

## Protocol Implementation

### Packet Types

| Command | Code | Description |
|---------|------|-------------|
| CONNECT | 0x0E | Establishes connection and sets page |
| BUILD | 0x8F | Sends subtitle row data (0x0F with parity) |
| REVEAL | 0x10 | Displays the buffered subtitle |
| CLEAR | 0x98 | Clears the subtitle display (0x18 with parity) |

### Transmission Sequence

1. **CONNECT** - Set the target teletext page
2. **CLEAR** - Clear any existing subtitle
3. **BUILD** - Send each subtitle row (first row with clear bit set)
4. **REVEAL** - Display the subtitle on screen

### Row Encoding

Row numbers are encoded as two Hamming 8/4 bytes representing the hexadecimal nibbles of the row number:
- Row 22 (0x16): Hamming(1), Hamming(6) → 0x02, 0x38
- Row 20 (0x14): Hamming(1), Hamming(4) → 0x02, 0x64

### WST Control Codes

| Code | Description |
|------|-------------|
| 0x0A | End Box |
| 0x0B | Start Box |
| 0x0C | Normal Height |
| 0x0D | Double Height |

## Row Layout

Subtitles use a spacing of 2 rows between lines for readability:

**Single Line (Lower position)**
- Line 1: Row 22

**Two Lines (Lower position)**
- Line 1: Row 20
- Line 2: Row 22

**Three Lines (Lower position)**
- Line 1: Row 18
- Line 2: Row 20
- Line 3: Row 22

## License

MIT License

Copyright (c) 2026 Christopher Glover

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Version History

- **3.4** - Use named constants for protocol commands
- **3.3** - Remove unused methods
- **3.2** - Fixed row spacing for single height subtitles
- **3.1** - Added NormalHeight (0x0C) control code for single height mode
- **3.0** - Always include height control code at start of each line
- **2.x** - Protocol fixes for row encoding, parity, and packet structure

## Acknowledgements

- Newfor protocol specification by AXON
- Compatible with FAB Subtitler and similar teletext inserters