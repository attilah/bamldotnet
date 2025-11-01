#!/bin/bash

# download-natives.sh
# Downloads native BAML binaries from GitHub releases and places them in the runtimes directory.

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

# GitHub repository information
OWNER="BoundaryML"
REPO="baml"
BASE_URL="https://github.com/$OWNER/$REPO/releases/download"

# Default values
VERSION=""
FORCE=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        -f|--force)
            FORCE=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Downloads native BAML binaries from GitHub releases."
            echo ""
            echo "Options:"
            echo "  -v, --version VERSION   Specific version to download (default: latest)"
            echo "  -f, --force            Force re-download even if files exist"
            echo "  -h, --help             Show this help message"
            echo ""
            echo "Examples:"
            echo "  $0                     # Download latest release"
            echo "  $0 -v 0.64.0           # Download version 0.64.0"
            echo "  $0 -v 0.64.0 -f        # Force re-download version 0.64.0"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

# Platform mappings: RID -> (asset name, binary filename)
PLATFORM_RIDS=("win-x64" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")
PLATFORM_ASSETS=("baml_cffi-x86_64-pc-windows-msvc.dll" "baml_cffi-aarch64-pc-windows-msvc.dll" "libbaml_cffi-x86_64-unknown-linux-gnu.so" "libbaml_cffi-aarch64-unknown-linux-gnu.so" "libbaml_cffi-x86_64-apple-darwin.dylib" "libbaml_cffi-aarch64-apple-darwin.dylib")
PLATFORM_BINARIES=("baml_cffi.dll" "baml_cffi.dll" "libbaml_cffi.so" "libbaml_cffi.so" "libbaml_cffi.dylib" "libbaml_cffi.dylib")

get_latest_release() {
    echo -e "${CYAN}Fetching latest BAML release from GitHub...${NC}" >&2
    local response
    response=$(curl -s -H "User-Agent: Baml.Net-Downloader" \
        "https://api.github.com/repos/$OWNER/$REPO/releases/latest")

    local latest_version
    latest_version=$(echo "$response" | grep -o '"tag_name": *"[^"]*"' | sed 's/"tag_name": *"\(.*\)"/\1/')

    if [ -z "$latest_version" ]; then
        echo -e "${RED}Failed to fetch latest release${NC}" >&2
        exit 1
    fi

    # Remove 'v' prefix if present
    latest_version=${latest_version#v}

    echo -e "${GREEN}Latest release: $latest_version${NC}" >&2
    echo "$latest_version"
}

update_directory_build_props() {
    local new_version="$1"
    local props_file="$(dirname "$(dirname "$0")")/Directory.Build.props"

    if [ ! -f "$props_file" ]; then
        echo -e "${RED}Directory.Build.props not found at: $props_file${NC}"
        exit 1
    fi

    echo -e "${CYAN}Updating Directory.Build.props with version $new_version...${NC}"

    # Use perl to update BamlNativeVersion (works on both macOS and Linux)
    perl -i -pe "s|<BamlNativeVersion>[^<]*</BamlNativeVersion>|<BamlNativeVersion>$new_version</BamlNativeVersion>|g" "$props_file"

    echo -e "${GREEN}Updated Directory.Build.props with BamlNativeVersion=$new_version${NC}"
}

download_binary() {
    local url="$1"
    local output_dir="$2"
    local binary_name="$3"

    echo -e "  ${GRAY}Downloading from $url...${NC}"

    # Ensure output directory exists
    mkdir -p "$output_dir"

    # Download directly to destination
    local dest_path="$output_dir/$binary_name"

    if ! curl -L -s -f -o "$dest_path" "$url"; then
        echo -e "  ${RED}Failed to download${NC}"
        rm -f "$dest_path"
        return 1
    fi

    echo -e "  ${GRAY}Binary saved to: $dest_path${NC}"

    # Verify file size
    local file_size=$(stat -f%z "$dest_path" 2>/dev/null || stat -c%s "$dest_path" 2>/dev/null || echo "unknown")
    if [ "$file_size" != "unknown" ]; then
        echo -e "  ${GRAY}Size: $file_size bytes${NC}"
    fi

    # Make executable on Unix platforms
    chmod +x "$dest_path" 2>/dev/null || true

    return 0
}

# Main script
echo -e "${CYAN}=====================================${NC}"
echo -e "${CYAN}BAML Native Binary Downloader${NC}"
echo -e "${CYAN}=====================================${NC}"
echo ""

# Determine version to download
if [ -z "$VERSION" ]; then
    VERSION=$(get_latest_release)
else
    # Remove 'v' prefix if present
    VERSION=${VERSION#v}
    echo -e "${GREEN}Using specified version: $VERSION${NC}"
fi

# Update Directory.Build.props
update_directory_build_props "$VERSION"

echo ""
echo -e "${CYAN}Downloading native binaries for version $VERSION...${NC}"
echo ""

success_count=0
failed_platforms=()

# Process each platform
for i in "${!PLATFORM_RIDS[@]}"; do
    rid="${PLATFORM_RIDS[$i]}"
    asset="${PLATFORM_ASSETS[$i]}"
    binary="${PLATFORM_BINARIES[$i]}"

    echo -e "${YELLOW}[$rid] Processing...${NC}"

    output_dir="$(dirname "$(dirname "$0")")/runtimes/$rid"
    binary_path="$output_dir/$binary"

    # Check if binary already exists and Force is not set
    if [ -f "$binary_path" ] && [ "$FORCE" = false ]; then
        echo -e "${GRAY}[$rid] Binary already exists. Use -f or --force to re-download.${NC}"
        ((success_count++))
        echo ""
        continue
    fi

    download_url="$BASE_URL/$VERSION/$asset"

    if download_binary "$download_url" "$output_dir" "$binary"; then
        echo -e "${GREEN}[$rid] SUCCESS${NC}"
        ((success_count++))
    else
        echo -e "${RED}[$rid] FAILED${NC}"
        failed_platforms+=("$rid")
    fi

    echo ""
done

# Summary
echo -e "${CYAN}=====================================${NC}"
echo -e "${CYAN}Download Summary${NC}"
echo -e "${CYAN}=====================================${NC}"

total_platforms=${#PLATFORM_RIDS[@]}
if [ $success_count -eq $total_platforms ]; then
    echo -e "${GREEN}Successfully downloaded: $success_count/$total_platforms${NC}"
else
    echo -e "${YELLOW}Successfully downloaded: $success_count/$total_platforms${NC}"
fi

if [ ${#failed_platforms[@]} -gt 0 ]; then
    echo -e "${YELLOW}Failed platforms: ${failed_platforms[*]}${NC}"
    echo -e "${YELLOW}Warning: Some platforms failed to download. Continuing anyway...${NC}"
fi

if [ $success_count -gt 0 ]; then
    echo ""
    echo -e "${GREEN}Downloaded $success_count platform(s) successfully!${NC}"
    echo ""
    echo -e "${CYAN}Next steps:${NC}"
    echo -e "${GRAY}  1. Enable package generation in Directory.Build.props:${NC}"
    echo -e "${GRAY}     <GeneratePackageOnBuild>true</GeneratePackageOnBuild>${NC}"
    echo -e "${GRAY}  2. Build NuGet packages:${NC}"
    echo -e "${GRAY}     dotnet pack Baml.Net.sln${NC}"
    exit 0
else
    echo -e "${RED}No platforms were downloaded successfully!${NC}"
    exit 1
fi
