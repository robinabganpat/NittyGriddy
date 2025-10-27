# NittyGriddy

**A friendly window grid manager for Windows**

NittyGriddy is a Windows application that provides a visual grid-based system for snapping windows into predefined layouts across multiple monitors. Perfect for poker players managing multiple tables, traders watching markets, or anyone who wants organized window layouts.

## Features

- **Multi-Monitor Support**: Configure independent grids for each connected display
- **Flexible Grid Layouts**:
  - Fixed Grid: Define custom rows × columns
  - N-Window Optimized: Automatically calculate optimal grid for N windows
- **Visual Grid Overlay**: Grid appears only when dragging windows, cells highlight based on mouse cursor position
- **Automatic Window Snapping**:
  - Drag windows to cells and they snap on release (uses mouse position for accurate cell detection)
  - New windows automatically snap to first available cell
  - Existing windows automatically re-snap when grid settings change
  - Cells can hold multiple windows (prefers empty cells first)
  - Auto-arrange button to distribute all matching windows across the grid
- **Window Filtering**: Target specific windows by class name or title pattern
- **Configuration Persistence**: Save and load grid configurations as JSON
- **Keyboard Shortcuts**:
  - `Ctrl+Shift+G`: Toggle grid overlay on/off
  - `Ctrl+Shift+S`: Snap active window to nearest cell

## Quick Start

1. Build and run the application:
   ```bash
   dotnet run --project App/App.csproj
   ```

2. Configure your grid:
   - **All connected monitors are shown simultaneously**
   - Each monitor has its own collapsible configuration section
   - For each monitor:
     - Choose layout mode (Fixed Grid or N-Window Optimized)
     - Set rows/columns or number of windows
     - Adjust cell spacing and margins
   - **Grid applies automatically as you adjust settings!**

3. Enable the grid system:
   - Click "Enable Grid" or press `Ctrl+Shift+G`
   - The UI will show how many target windows match your filters

4. Use the window snapping:
   - **Drag existing windows**: Grid appears, cells highlight based on mouse position, release to snap
   - **Open new windows**: Automatically snap to the first available cell!
   - **Auto-arrange all windows**: Click "Auto-Arrange Windows" to distribute all matching windows across the grid

## Default Configuration

By default, the app targets windows with class name `ApolloRuntimeContentWindow` (common poker clients). You can add additional window classes or title patterns in the "Window Filters" tab.

### Adding Window Filters

**Easy Method - Pick Window:**
1. Go to the "Window Filters" tab
2. Click "Pick Window from List..."
3. Select the window you want to track from the list
4. Choose whether to add the class name, title, or both
5. The filter is automatically added!

**Manual Method:**
- Type the window class name or title pattern directly in the text boxes

## Example Configurations

### 6-Table Fixed Grid (3×2)
- Mode: Fixed Grid
- Rows: 2, Columns: 3
- Use case: Six poker tables in 2 rows

### 9-Table Optimized
- Mode: N-Window Optimized
- Number of Windows: 9
- Result: Automatically creates 3×3 grid

## Architecture

See [CLAUDE.md](CLAUDE.md) for detailed architecture documentation.

## Requirements

- Windows OS
- .NET 9.0 runtime or later
- Multiple monitors (optional, works with single monitor too)

## License

MIT
