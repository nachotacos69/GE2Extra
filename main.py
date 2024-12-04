from PRES import PRESFile
import os


def process_nested_res(file_path, rdp_files):
    """
    Process a nested PRES archive.
    """
    output_folder = file_path.rsplit(".", 1)[0]  
    os.makedirs(output_folder, exist_ok=True)

    try:
        pres_file = PRESFile(file_path, rdp_files)
        pres_file.parse_header()
        pres_file.parse_entry_groups()
        pres_file.extract_files(output_folder)
        print(f"[INFO] Nested .res file '{file_path}' processed successfully.")
    except Exception as e:
        print(f"[ERROR] Failed to process nested .res file '{file_path}': {str(e)}")


def load_rdp_files():
    """Returns a dictionary of RDP file paths."""
    rdp_files = {}
    for rdp_name in ["package", "data", "patch"]:
        rdp_path = f"{rdp_name}.rdp"
        if os.path.exists(rdp_path):
            rdp_files[rdp_name] = rdp_path
        else:
            print(f"[WARNING] Missing {rdp_path}. Files requiring this may fail.")
    return rdp_files


def main():
    print("Select an option:")
    print("1. Extract .res file")
    print("2. Repack (placeholder)")
    choice = input("Enter your choice (1 or 2): ")

    if choice == "1":
        file_path = input("Enter the .res file path: ")
        output_folder = file_path.rsplit(".", 1)[0]  # Use the file name without extension

        rdp_files = load_rdp_files()

        try:
            pres_file = PRESFile(file_path, rdp_files)
            pres_file.parse_header()
            pres_file.parse_entry_groups()
            extracted_files = pres_file.extract_files(output_folder)

            # Handle nested .res files
            for file_info in extracted_files:
                if file_info.get("is_res"):
                    process_nested_res(file_info["path"], rdp_files)

            print(f"Extraction complete. Files saved in {output_folder}.")
        except Exception as e:
            print(f"[ERROR] {str(e)}")
    elif choice == "2":
        print("Repacking feature is not implemented yet.")
    else:
        print("Invalid choice. Exiting.")


if __name__ == "__main__":
    main()
